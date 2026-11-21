using System.Text.Json;
using Application.Common;
using Application.DTOs.Payment;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Interfaces.Services.Booking;
using Application.Interfaces.Services.Payment;
using Domain.Entity;
using Domain.Enum;
using Infrastructure.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Net.payOS.Types;

namespace Infrastructure.Services.Payment
{
    public class PaymentService : IPaymentService
    {
        private readonly IPaymentOrderRepository _paymentOrderRepository;

        private readonly ICustomerProfileRepository _customerProfileRepository;

        private readonly IPaymentGatewayFactory _paymentGatewayFactory;

        private readonly IBookingRepository _bookingRepository;

        private readonly IWalletService _walletService;

        private readonly IUnitOfWork _unitOfWork;

        private readonly IBookingService _bookingService;

        private readonly INotificationService _notificationService;

        private readonly IHubContext<NotificationHub> _hubContext;

        private readonly ILogger<PaymentService> _logger;

        private readonly IWorkerProfileRepository _workerProfileRepository;

        private readonly IWorkerServiceRepository _workerServiceRepository;

        private readonly Application.Settings.SePaySettings _sePaySettings;

        public PaymentService(
            IPaymentOrderRepository paymentOrderRepository,
            ICustomerProfileRepository customerProfileRepository,
            IPaymentGatewayFactory paymentGatewayFactory,
            IBookingRepository bookingRepository,
            IWalletService walletService,
            IUnitOfWork unitOfWork,
            IBookingService bookingService,
            INotificationService notificationService,
            IHubContext<NotificationHub> hubContext,
            ILogger<PaymentService> logger,
            IWorkerProfileRepository workerProfileRepository,
            IWorkerServiceRepository workerServiceRepository,
            Microsoft.Extensions.Options.IOptions<Application.Settings.SePaySettings> sePayOptions
        )
        {
            _sePaySettings = sePayOptions.Value;

            _paymentOrderRepository = paymentOrderRepository;

            _customerProfileRepository = customerProfileRepository;

            _paymentGatewayFactory = paymentGatewayFactory;

            _bookingRepository = bookingRepository;

            _walletService = walletService;

            _unitOfWork = unitOfWork;

            _bookingService = bookingService;

            _notificationService = notificationService;

            _hubContext = hubContext;

            _logger = logger;

            _workerProfileRepository = workerProfileRepository;

            _workerServiceRepository = workerServiceRepository;
        }

        public async Task<OperationResult<string>> CreateTopUpPaymentUrlAsync(
            Guid userId,
            long amount,
            PaymentMethod method,
            CancellationToken cancellationToken
        )
        {
            if (amount <= 0)
            {
                return OperationResult<string>.Failure("Invalid amount");
            }

            var order = new PaymentOrder
            {
                UserId = userId,

                Amount = amount,
                DiscountAmount = 0,
                FinalAmount = amount,

                Method = method,
                Status = PaymentStatus.Pending,

                // Chỉ KTV được nạp ví (controller đã chặn vai khách) → cộng vào ví KTV.
                Type = PaymentOrderType.WorkerWalletTopUp,
            };

            await _paymentOrderRepository.AddAsync(order, cancellationToken);

            var (paymentUrl, gatewayError) = await CreateGatewayUrlAsync(method, order, cancellationToken);
            if (gatewayError != null)
                return OperationResult<string>.Failure(gatewayError);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return OperationResult<string>.Success(paymentUrl);
        }

        public async Task<OperationResult<string>> CreateBookingPaymentUrlAsync(
            Guid bookingId,
            Guid userId,
            PaymentMethod method,
            CancellationToken cancellationToken
        )
        {
            var booking = await _bookingRepository.GetByIdAsync(bookingId, cancellationToken);

            if (booking == null)
            {
                return OperationResult<string>.Failure("Booking not found");
            }

            var customerProfile = await _customerProfileRepository.GetByUserIdAsync(
                userId,
                cancellationToken
            );

            if (customerProfile == null)
            {
                return OperationResult<string>.Failure("Customer profile not found");
            }

            if (booking.CustomerProfileId != customerProfile.Id)
            {
                return OperationResult<string>.Failure("Forbidden");
            }

            if (booking.FinalPrice == null || booking.FinalPrice <= 0)
            {
                return OperationResult<string>.Failure("Invalid booking price");
            }



            var existedOrder = await _paymentOrderRepository.GetBookingPaymentOrderAsync(
                bookingId,
                cancellationToken
            );

            if (existedOrder != null && existedOrder.Status == PaymentStatus.Paid)
            {
                return OperationResult<string>.Failure("Booking already paid");
            }

            // When retrying an online payment (MoMo, VnPay, PayOS), gateways reject
            
            if (existedOrder != null && existedOrder.Status != PaymentStatus.Paid
                && method != PaymentMethod.Cash)
            {
                _paymentOrderRepository.Remove(existedOrder);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                existedOrder = null;
            }

            PaymentOrder order;

            if (existedOrder != null)
            {
                order = existedOrder;

                order.Method = method;

                order.Status = PaymentStatus.Pending;

                _paymentOrderRepository.Update(order);
            }
            else
            {
                order = new PaymentOrder
                {
                    BookingId = booking.Id,

                    UserId = userId,

                    Amount = booking.FinalPrice.Value,

                    DiscountAmount = 0,

                    FinalAmount = booking.FinalPrice.Value,

                    Method = method,

                    Status = PaymentStatus.Pending,

                    Type = PaymentOrderType.BookingPayment,
                };

                await _paymentOrderRepository.AddAsync(order, cancellationToken);
            }

            // Cash payments don't go through an online gateway.
            // The order stays Pending until cash is actually collected after the service.
            if (method == PaymentMethod.Cash)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                if (order.BookingId.HasValue)
                {
                    await _bookingService.ConfirmPaymentAsync(
                        order.BookingId.Value,
                        cancellationToken
                    );
                }

                return OperationResult<string>.Success(string.Empty);
            }

            var (paymentUrl, gatewayError) = await CreateGatewayUrlAsync(method, order, cancellationToken);
            if (gatewayError != null)
                return OperationResult<string>.Failure(gatewayError);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return OperationResult<string>.Success(paymentUrl);
        }

        public async Task<OperationResult<bool>> HandleCallbackAsync(
            PaymentMethod method,
            Dictionary<string, string> response,
            CancellationToken cancellationToken
        )
        {
            var gateway = _paymentGatewayFactory.Get(method);

            var valid = gateway.VerifySignature(response);

            if (!valid)
            {
                return OperationResult<bool>.Failure("Invalid signature");
            }

            PaymentOrder? order = null;

            string? transactionId = null;

            bool paymentSuccess = false;

            long? paidAmount = null;

            switch (method)
            {
                case PaymentMethod.Vnpay:
                {
                    var orderId = Guid.Parse(response["vnp_TxnRef"]);

                    order = await _paymentOrderRepository.GetByIdAsync(orderId, cancellationToken);

                    transactionId = response["vnp_TransactionNo"];

                    paymentSuccess = response["vnp_ResponseCode"] == "00";

                    // vnp_Amount = số tiền x 100
                    if (
                        response.TryGetValue("vnp_Amount", out var vnpAmount)
                        && long.TryParse(vnpAmount, out var vnpAmountValue)
                    )
                    {
                        paidAmount = vnpAmountValue / 100;
                        if (vnpAmountValue % 100 != 0)
                            paidAmount = -1; // không hợp lệ -> chắc chắn lệch
                    }

                    break;
                }

                case PaymentMethod.Momo:
                {
                    var orderId = Guid.Parse(response["orderId"]);

                    order = await _paymentOrderRepository.GetByIdAsync(orderId, cancellationToken);

                    transactionId = response["transId"];

                    paymentSuccess = response["resultCode"] == "0";

                    if (
                        response.TryGetValue("amount", out var momoAmount)
                        && long.TryParse(momoAmount, out var momoAmountValue)
                    )
                    {
                        paidAmount = momoAmountValue;
                    }

                    break;
                }

                default:

                    return OperationResult<bool>.Failure("Unsupported payment method");
            }

            if (order == null)
            {
                return OperationResult<bool>.Failure("Payment order not found");
            }

            if (order.Status == PaymentStatus.Paid)
            {
                return OperationResult<bool>.Success(true, "Payment already processed");
            }

            if (!paymentSuccess)
            {
                order.Status = PaymentStatus.Failed;

                _paymentOrderRepository.Update(order);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return OperationResult<bool>.Failure("Payment failed");
            }

            if (paidAmount != order.FinalAmount)
            {
                _logger.LogWarning(
                    "Payment amount mismatch ({Method}) for order {OrderId}: gateway={PaidAmount}, expected={Expected}. Payment NOT recorded.",
                    method,
                    order.Id,
                    paidAmount,
                    order.FinalAmount
                );

                order.GatewayResponse = JsonSerializer.Serialize(response);
                _paymentOrderRepository.Update(order);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return OperationResult<bool>.Failure("Số tiền thanh toán không khớp với đơn hàng");
            }

            order.Status = PaymentStatus.Paid;

            order.PaidAt = DateTime.UtcNow;

            order.ExternalTransactionId = transactionId;

            order.GatewayResponse = JsonSerializer.Serialize(response);

            _paymentOrderRepository.Update(order);

            await ProcessSuccessfulPaymentAsync(order, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return OperationResult<bool>.Success(true, "Payment success");
        }

        private async Task ProcessSuccessfulPaymentAsync(
            PaymentOrder order,
            CancellationToken cancellationToken
        )
        {
            switch (order.Type)
            {
                case PaymentOrderType.WalletTopUp:

                    await _walletService.TopUpAsync(
                        order.UserId,
                        order.FinalAmount,
                        $"{order.Method} Topup #{order.Id}",
                        cancellationToken
                    );

                    break;

                case PaymentOrderType.WorkerWalletTopUp:

                    await _walletService.TopUpAsync(
                        order.UserId,
                        order.FinalAmount,
                        $"{order.Method} Topup #{order.Id}",
                        cancellationToken,
                        WalletOwnerType.Worker
                    );

                    break;

                case PaymentOrderType.BookingPayment:

                    if (order.BookingId.HasValue)
                    {
                        await _bookingService.ConfirmPaymentAsync(
                            order.BookingId.Value,
                            cancellationToken
                        );
                    }

                    break;

                case PaymentOrderType.WorkerDeposit:

                    await _walletService.DepositLockAsync(
                        order.UserId,
                        order.FinalAmount,
                        $"Deposit #{order.Id}",
                        cancellationToken
                    );

                    // Cập nhật WorkerProfile: đánh dấu đã nạp cọc & kích hoạt nhận việc
                    var workerProfile = await _workerProfileRepository.FirstOrDefaultAsync(
                        wp => wp.UserId == order.UserId,
                        cancellationToken
                    );

                    if (workerProfile != null)
                    {
                        workerProfile.IsDepositPaid = true;
                        workerProfile.DepositPaidAt = DateTime.UtcNow;
                        workerProfile.IsAcceptingJobs = true;
                        workerProfile.IsOnline = true;
                        _workerProfileRepository.Update(workerProfile);
                    }

                    break;

                default:

                    throw new Exception($"Unsupported payment type: {order.Type}");
            }
        }

        public async Task<OperationResult<bool>> HandlePayOSCallbackAsync(
            PayOSCallbackDto callback,
            CancellationToken cancellationToken
        )
        {
            try
            {
                if (callback == null || callback.Data == null || callback.Data.OrderCode <= 0)
                {
                    _logger.LogInformation("PayOS webhook received empty/null callback or data payload. Acknowledging as test ping.");
                    return OperationResult<bool>.Success(true, "Callback acknowledged");
                }

                var data = callback.Data;

                // 1. Verify Webhook Signature using PayOS SDK (chống giả mạo request)
                try
                {
                    var payosService = _paymentGatewayFactory.Get(PaymentMethod.PayOS) as PayOSService
                        ?? throw new InvalidOperationException("PayOS gateway is not configured");

                    var jsonString = JsonSerializer.Serialize(callback);
                    var webhookBody = JsonSerializer.Deserialize<WebhookType>(jsonString, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? throw new InvalidOperationException("PayOS webhook body is empty");

                    payosService.VerifyWebhookData(webhookBody);
                    _logger.LogInformation("PayOS webhook signature verified for OrderCode: {OrderCode}", data.OrderCode);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PayOS webhook signature verification failed for OrderCode: {OrderCode}", data.OrderCode);

                    // Check if this is a test webhook (order code not found in DB)
                    var testCheckOrder = await _paymentOrderRepository.GetByGatewayOrderCodeAsync(
                        data.OrderCode,
                        cancellationToken
                    );
                    if (testCheckOrder == null)
                    {
                        _logger.LogInformation("PayOS test webhook ping detected (order {OrderCode} not found). Returning 200 OK.", data.OrderCode);
                        return OperationResult<bool>.Success(true, "Test webhook acknowledged");
                    }

                    return OperationResult<bool>.Failure("Invalid PayOS webhook signature");
                }

                // 2. Lookup the payment order
                var order = await _paymentOrderRepository.GetByGatewayOrderCodeAsync(
                    data.OrderCode,
                    cancellationToken
                );

                if (order == null)
                {
                    _logger.LogInformation("PayOS webhook: Payment order not found for OrderCode {OrderCode}. Returning 200 OK for test request.", data.OrderCode);
                    return OperationResult<bool>.Success(true, "Order not found, test request acknowledged");
                }

                // 3. Idempotency check — already processed
                if (order.Status == PaymentStatus.Paid)
                    return OperationResult<bool>.Success(true, "Already processed");

                var isSuccess = data.Code == "00";

                order.GatewayResponse = JsonSerializer.Serialize(callback);

                if (!isSuccess)
                {
                    order.Status = PaymentStatus.Failed;
                    _paymentOrderRepository.Update(order);

                    await _unitOfWork.SaveChangesAsync(cancellationToken);

                    return OperationResult<bool>.Success(false, "Payment failed status recorded");
                }

            // 4. Đối chiếu số tiền với đơn hàng
            if (data.Amount != order.FinalAmount)
            {
                _logger.LogWarning(
                    "PayOS amount mismatch for order {OrderId} (OrderCode {OrderCode}): gateway={PaidAmount}, expected={Expected}. Payment NOT recorded.",
                    order.Id,
                    data.OrderCode,
                    data.Amount,
                    order.FinalAmount
                );

                _paymentOrderRepository.Update(order);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return OperationResult<bool>.Failure("Số tiền thanh toán không khớp với đơn hàng");
            }

            // 5. Mark as Paid
            order.Status = PaymentStatus.Paid;
            order.PaidAt = DateTime.UtcNow;
            order.ExternalTransactionId = data.OrderCode.ToString();

            // Store the bank reference code for audit trail
            if (!string.IsNullOrEmpty(data.Reference))
            {
                order.GatewayRef = data.Reference;
            }

            _paymentOrderRepository.Update(order);

            // 5. Process business logic (TopUp wallet or Confirm booking)
            //    IMPORTANT: Do this BEFORE SaveChangesAsync so both PaymentOrder update
            //    and Booking status change are committed atomically.
            await ProcessSuccessfulPaymentAsync(order, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await NotifyPaymentSuccessAsync(order, data.Reference, data.OrderCode.ToString(), cancellationToken);

            return OperationResult<bool>.Success(true, "Payment success");
            }
            catch (Exception outerEx)
            {
                _logger.LogError(outerEx, "Unhandled exception in HandlePayOSCallbackAsync. Returning 200 OK to prevent webhook error 500.");
                return OperationResult<bool>.Success(true, "Handled outer exception in webhook");
            }
        }

        public async Task<OperationResult<string>> CreateWorkerDepositPaymentUrlAsync(
            Guid workerUserId,
            PaymentMethod method,
            CancellationToken cancellationToken
        )
        {
            // 1. Tìm WorkerProfile
            var workerProfile = await _workerProfileRepository.FirstOrDefaultAsync(
                wp => wp.UserId == workerUserId,
                cancellationToken
            );

            if (workerProfile == null)
                return OperationResult<string>.Failure("Worker profile not found");

            if (workerProfile.IsDepositPaid)
                return OperationResult<string>.Failure("Deposit already paid");

            // 2. Tìm dịch vụ Spa chính (IsPrimary) để lấy BasePrice làm mức cọc
            var primaryService = await _workerServiceRepository.FirstOrDefaultAsync(
                ws => ws.WorkerProfileId == workerProfile.Id && ws.IsPrimary,
                cancellationToken
            );

            if (primaryService == null)
                return OperationResult<string>.Failure("Primary service not found. Please set up your primary spa service first.");

            var depositAmount = primaryService.BasePrice;

            if (depositAmount <= 0)
                return OperationResult<string>.Failure("Invalid deposit amount from primary service price");

            // Cập nhật DepositRequiredAmount trên WorkerProfile
            workerProfile.DepositRequiredAmount = depositAmount;
            _workerProfileRepository.Update(workerProfile);

            // 3. Tạo PaymentOrder loại WorkerDeposit
            var order = new PaymentOrder
            {
                UserId = workerUserId,
                Amount = depositAmount,
                DiscountAmount = 0,
                FinalAmount = depositAmount,
                Method = method,
                Status = PaymentStatus.Pending,
                Type = PaymentOrderType.WorkerDeposit,
            };

            await _paymentOrderRepository.AddAsync(order, cancellationToken);

            // 4. Sinh mã VietQR / Checkout URL qua cổng thanh toán
            var (paymentUrl, gatewayError) = await CreateGatewayUrlAsync(method, order, cancellationToken);
            if (gatewayError != null)
                return OperationResult<string>.Failure(gatewayError);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return OperationResult<string>.Success(paymentUrl);
        }

        /// <summary>Gọi cổng thanh toán; lỗi cấu hình (vd. SePay chưa điền tài khoản) trả về thông báo thay vì lỗi 500.</summary>
        private async Task<(string Url, string? Error)> CreateGatewayUrlAsync(
            PaymentMethod method,
            PaymentOrder order,
            CancellationToken cancellationToken
        )
        {
            try
            {
                var gateway = _paymentGatewayFactory.Get(method);
                return (await gateway.CreatePaymentUrlAsync(order, cancellationToken), null);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Payment gateway {Method} is not configured", method);
                return (string.Empty, "Phương thức thanh toán này tạm thời chưa dùng được. Vui lòng chọn cách khác.");
            }
        }

        /// <summary>
        /// Tiền chuyển khoản về qua SePay (TransferType = "in"). Khớp mã TZxxxxxxxxxx trong nội dung
        /// với PaymentOrder.GatewayOrderCode, đối chiếu ĐÚNG số tiền, rồi ghi nhận đã thanh toán.
        /// Token webhook là BẮT BUỘC: không cấu hình token = từ chối (ai cũng giả được "tiền về").
        /// </summary>
        public async Task<OperationResult<bool>> HandleSePayIncomingAsync(
            SePayWebhookDto webhook,
            string? authorizationHeader,
            CancellationToken cancellationToken
        )
        {
            if (!SePayGatewayService.IsValidWebhookToken(_sePaySettings.WebhookToken, authorizationHeader))
            {
                _logger.LogWarning("SePay incoming webhook rejected: invalid or unconfigured token");
                return OperationResult<bool>.Failure("Unauthorized");
            }

            var code = SePayGatewayService.TryParseOrderCode(webhook.Content, _sePaySettings.CodePrefix);
            if (code == null)
                return OperationResult<bool>.Success(true, "Not a TAYZEN transfer");

            var order = await _paymentOrderRepository.GetByGatewayOrderCodeAsync(code.Value, cancellationToken);
            if (order == null || order.Method != PaymentMethod.SePay)
            {
                _logger.LogWarning("SePay incoming: no SePay order for code {Code} (txn {TxnId})", code, webhook.Id);
                return OperationResult<bool>.Success(true, "Order not found");
            }

            if (order.Status == PaymentStatus.Paid)
                return OperationResult<bool>.Success(true, "Already processed");

            order.GatewayResponse = JsonSerializer.Serialize(webhook);

            if (webhook.TransferAmount != order.FinalAmount)
            {
                _logger.LogWarning(
                    "SePay amount mismatch for order {OrderId} (code {Code}): received={Received}, expected={Expected}. NOT marked paid.",
                    order.Id, code, webhook.TransferAmount, order.FinalAmount);
                _paymentOrderRepository.Update(order);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return OperationResult<bool>.Success(true, "Amount mismatch recorded");
            }

            order.Status = PaymentStatus.Paid;
            order.PaidAt = DateTime.UtcNow;
            order.ExternalTransactionId = webhook.Id.ToString();
            order.GatewayRef = webhook.ReferenceCode;
            _paymentOrderRepository.Update(order);

            await ProcessSuccessfulPaymentAsync(order, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await NotifyPaymentSuccessAsync(order, webhook.ReferenceCode, code.Value.ToString(), cancellationToken);
            return OperationResult<bool>.Success(true, "Payment success");
        }

        private async Task NotifyPaymentSuccessAsync(
            PaymentOrder order,
            string? reference,
            string orderCodeForLog,
            CancellationToken cancellationToken
        )
        {
            try
            {
                await _hubContext.Clients
                    .User(order.UserId.ToString())
                    .SendAsync("PaymentCompleted", new
                    {
                        OrderId = order.Id,
                        BookingId = order.BookingId,
                        Amount = order.FinalAmount,
                        Status = "Paid",
                        Reference = reference
                    }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send SignalR PaymentCompleted to user {UserId}", order.UserId);
            }

            try
            {
                var notifTitle = order.Type switch
                {
                    PaymentOrderType.WalletTopUp => "Nạp ví thành công!",
                    PaymentOrderType.WorkerWalletTopUp => "Nạp số dư bảo đảm thành công!",
                    PaymentOrderType.WorkerDeposit => "Nạp cọc kích hoạt thành công!",
                    _ => "Thanh toán thành công!"
                };

                var notifBody = order.Type switch
                {
                    PaymentOrderType.WalletTopUp => $"Bạn đã nạp thành công {order.FinalAmount:N0}đ vào ví TAYZEN.",
                    PaymentOrderType.WorkerWalletTopUp => $"Bạn đã nạp {order.FinalAmount:N0}đ vào ví KTV. Số dư này bảo đảm phí nền tảng cho đơn tiền mặt.",
                    PaymentOrderType.WorkerDeposit => $"Bạn đã nạp {order.FinalAmount:N0}đ số dư bảo đảm thành công.",
                    _ => $"Đơn dịch vụ của bạn đã được thanh toán {order.FinalAmount:N0}đ và đang chờ Kỹ thuật viên tiếp nhận."
                };

                var deepLink = order.BookingId.HasValue
                    ? $"/booking-detail?bookingId={order.BookingId.Value}"
                    : "/wallet";

                await _notificationService.SendNotificationAsync(
                    order.UserId,
                    NotificationType.Payment,
                    notifTitle,
                    notifBody,
                    deepLink,
                    new { orderId = order.Id, bookingId = order.BookingId, amount = order.FinalAmount },
                    null,
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send payment notification for OrderCode {OrderCode}", orderCodeForLog);
            }
        }
    }
}

