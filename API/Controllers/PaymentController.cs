using System.Text.Json;
using Application.DTOs.Payment;
using Application.Interfaces.Services;
using Application.Interfaces.Services.Payment;
using Domain.Enum;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    [ApiController]
    [Route("api/payment")]
    public class PaymentController : ApiController
    {
        private readonly IPaymentService _paymentService;
        private readonly IPayoutService _payoutService;

        public PaymentController(IPaymentService paymentService, IPayoutService payoutService)
        {
            _paymentService = paymentService;
            _payoutService = payoutService;
        }

        /// <summary>
        /// Nạp số dư ví. CEO 31/10: chỉ KTV được nạp (số dư bảo đảm cho đơn tiền mặt).
        /// Khách hàng không được nạp ví (giữ tiền nạp trước cần giấy phép ví điện tử) —
        /// khách vẫn dùng được số dư cũ để thanh toán đơn.
        /// </summary>
        [Authorize]
        [HttpPost("topup")]
        public async Task<IActionResult> CreateTopUpPayment(
            [FromBody] CreateTopupPaymentRequestDto request,
            CancellationToken cancellationToken
        )
        {
            if (!User.IsInRole("WORKER"))
            {
                return HandleResult(
                    Application.Common.OperationResult<string>.Failure(
                        "TAYZEN không nhận nạp tiền vào ví khách hàng. Vui lòng thanh toán từng đơn."
                    )
                );
            }

            var result = await _paymentService.CreateTopUpPaymentUrlAsync(
                GetUserId(),
                request.Amount,
                request.Method,
                cancellationToken
            );

            return HandleResult(result);
        }

        [Authorize(Roles = "WORKER")]
        [HttpPost("worker/deposit")]
        public async Task<IActionResult> CreateWorkerDepositPayment(
            [FromBody] CreateWorkerDepositPaymentRequestDto request,
            CancellationToken cancellationToken
        )
        {
            var result = await _paymentService.CreateWorkerDepositPaymentUrlAsync(
                GetUserId(),
                request.Method,
                cancellationToken
            );

            return HandleResult(result);
        }

        [Authorize(Roles = "CUSTOMER")]
        [HttpPost("booking/{bookingId}")]
        public async Task<IActionResult> CreateBookingPayment(
            Guid bookingId,
            [FromBody] CreateBookingPaymentRequestDto request,
            CancellationToken cancellationToken
        )
        {
            var result = await _paymentService.CreateBookingPaymentUrlAsync(
                bookingId,
                GetUserId(),
                request.Method,
                cancellationToken
            );

            return HandleResult(result);
        }

        [HttpGet("callback/vnpay")]
        public async Task<IActionResult> HandleVnPayCallback(CancellationToken cancellationToken)
        {
            var response = Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString());

            var result = await _paymentService.HandleCallbackAsync(
                PaymentMethod.Vnpay,
                response,
                cancellationToken
            );

            return HandleResult(result);
        }

        [HttpGet("callback/momo")]
        public async Task<IActionResult> HandleMoMoCallback(CancellationToken cancellationToken)
        {
            var response = Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString());

            var result = await _paymentService.HandleCallbackAsync(
                PaymentMethod.Momo,
                response,
                cancellationToken
            );

            return HandleResult(result);
        }

        [HttpPost("callback/momo")]
        public async Task<IActionResult> HandleMoMoIpn(
            [FromBody] JsonElement jsonBody,
            CancellationToken cancellationToken
        )
        {
            var response = new Dictionary<string, string>();
            foreach (var prop in jsonBody.EnumerateObject())
            {
                response[prop.Name] = prop.Value.ToString();
            }

            var result = await _paymentService.HandleCallbackAsync(
                PaymentMethod.Momo,
                response,
                cancellationToken
            );

            return HandleResult(result);
        }

        [HttpPost("callback/payos")]
        public async Task<IActionResult> HandlePayOSCallback(
            [FromBody] PayOSCallbackDto callback,
            CancellationToken cancellationToken
        )
        {
            var result = await _paymentService.HandlePayOSCallbackAsync(
                callback,
                cancellationToken
            );

            return HandleResult(result);
        }

        [HttpPost("webhook/sepay")]
        public async Task<IActionResult> HandleSePayWebhook(
            [FromBody] SePayWebhookDto webhook,
            [FromHeader(Name = "Authorization")] string? authorizationHeader,
            CancellationToken cancellationToken
        )
        {
            var result = await _payoutService.ProcessSePayWebhookAsync(
                webhook,
                authorizationHeader,
                cancellationToken
            );

            return HandleResult(result);
        }
    }
}
