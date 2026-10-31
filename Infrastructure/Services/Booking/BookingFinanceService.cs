using System.Globalization;
using Application.Common;
using Application.DTOs.Booking;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services.Booking;
using Application.Settings;
using Domain.Entity;
using Domain.Enum;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BookingEntity = Domain.Entity.Booking;

namespace Infrastructure.Services.Booking
{
    /// <inheritdoc />
    public class BookingFinanceService : IBookingFinanceService
    {
        private readonly IWalletRepository _walletRepository;
        private readonly IWalletTransactionRepository _walletTransactionRepository;
        private readonly IBookingVoucherRepository _bookingVoucherRepository;
        private readonly CommissionSettings _settings;
        private readonly ILogger<BookingFinanceService> _logger;

        public BookingFinanceService(
            IWalletRepository walletRepository,
            IWalletTransactionRepository walletTransactionRepository,
            IBookingVoucherRepository bookingVoucherRepository,
            IOptions<CommissionSettings> settings,
            ILogger<BookingFinanceService> logger
        )
        {
            _walletRepository = walletRepository;
            _walletTransactionRepository = walletTransactionRepository;
            _bookingVoucherRepository = bookingVoucherRepository;
            _settings = settings?.Value ?? new CommissionSettings();
            _logger = logger;
        }

        public async Task<long> GetServicePriceBeforeVoucherAsync(
            BookingEntity booking,
            CancellationToken cancellationToken = default
        )
        {
            var discount = await GetVoucherDiscountAsync(booking, cancellationToken);
            var finalPrice = booking.FinalPrice ?? booking.EstimatedPrice ?? 0;
            return Math.Max(0, finalPrice + discount);
        }

        public async Task SettleCompletedBookingAsync(
            BookingEntity booking,
            CancellationToken cancellationToken = default
        )
        {
            var servicePrice = await GetServicePriceBeforeVoucherAsync(booking, cancellationToken);
            var breakdown = PlatformFeeCalculator.Calculate(servicePrice, _settings);

            booking.PlatformFee = breakdown.PlatformFee;
            booking.PlatformFeeVat = breakdown.PlatformFeeVat;
            booking.WorkerNet = breakdown.WorkerNet;

            if (booking.WorkerProfile == null || booking.PaymentOrder == null)
            {
                _logger.LogWarning(
                    "Booking {BookingId}: thiếu KTV hoặc PaymentOrder, chỉ chốt phí, không quyết toán ví",
                    booking.Id
                );
                return;
            }

            var wallet = await _walletRepository.GetByUserIdAsync(
                booking.WorkerProfile.UserId,
                WalletOwnerType.Worker,
                cancellationToken
            );

            if (wallet == null)
            {
                _logger.LogWarning("Booking {BookingId}: không tìm thấy ví KTV", booking.Id);
                return;
            }

            if (booking.PaymentOrder.Method == PaymentMethod.Cash)
            {
                // Đơn tiền mặt: KTV đã cầm tiền khách — phần quyết toán ví do SettleCashAsync xử lý.
                await SettleCashAsync(booking, wallet, breakdown, servicePrice, cancellationToken);
                return;
            }

            if (booking.PaymentOrder.Status != PaymentStatus.Paid)
            {
                _logger.LogWarning(
                    "Booking {BookingId}: PaymentOrder chưa Paid ({Status}), không cộng ví KTV",
                    booking.Id,
                    booking.PaymentOrder.Status
                );
                return;
            }

            // Đơn trả trước: nền tảng thu hộ toàn bộ → cộng WorkerNet vào ví KTV (một lần).
            var alreadyCredited = await _walletTransactionRepository.ExistsAsync(
                x =>
                    x.PaymentOrderId == booking.PaymentOrder.Id
                    && x.Type == WalletTransactionType.BookingIncome,
                cancellationToken
            );
            if (alreadyCredited || breakdown.WorkerNet <= 0)
            {
                return;
            }

            await CreditAsync(
                wallet,
                breakdown.WorkerNet,
                WalletTransactionType.BookingIncome,
                booking,
                cancellationToken,
                platformFee: breakdown.TotalFee,
                countAsEarning: true
            );
        }

        /// <summary>
        /// Đơn tiền mặt hoàn thành: KTV đã thu tiền khách (giá sau voucher).
        /// - Khoản tạm giữ lúc nhận đơn chuyển thành phí chính thức (giao dịch đổi sang Fee/Success);
        ///   chênh lệch (nếu giá đổi sau khi nhận đơn) thì trừ thêm hoặc hoàn lại.
        /// - Đơn nhận trước khi có ký quỹ (không có khoản giữ): trừ phí + VAT ngay, ví có thể âm (công nợ).
        /// - Voucher do nền tảng chịu: cộng phần giảm giá vào ví KTV.
        /// </summary>
        private async Task SettleCashAsync(
            BookingEntity booking,
            Wallet wallet,
            PlatformFeeBreakdown breakdown,
            long servicePrice,
            CancellationToken cancellationToken
        )
        {
            var holdTx = await FindPendingHoldAsync(booking, cancellationToken);
            long charged = 0;

            if (holdTx != null)
            {
                holdTx.Type = WalletTransactionType.Fee;
                holdTx.Status = TransactionStatus.Success;
                holdTx.PlatformFee = breakdown.TotalFee;
                _walletTransactionRepository.Update(holdTx);
                charged = holdTx.Amount;
            }
            else if (booking.HeldPlatformFee > 0)
            {
                _logger.LogWarning(
                    "Booking {BookingId}: HeldPlatformFee={Held} nhưng không thấy giao dịch giữ phí",
                    booking.Id,
                    booking.HeldPlatformFee
                );
                charged = booking.HeldPlatformFee.Value;
            }

            var difference = breakdown.TotalFee - charged;
            if (difference > 0)
            {
                await DebitAsync(
                    wallet,
                    difference,
                    WalletTransactionType.Fee,
                    TransactionStatus.Success,
                    booking,
                    cancellationToken,
                    platformFee: breakdown.TotalFee
                );
            }
            else if (difference < 0)
            {
                await CreditAsync(
                    wallet,
                    -difference,
                    WalletTransactionType.PlatformFeeRelease,
                    booking,
                    cancellationToken
                );
            }

            booking.HeldPlatformFee = 0;

            var voucherDiscount = servicePrice - (booking.FinalPrice ?? servicePrice);
            if (voucherDiscount > 0)
            {
                await CreditAsync(
                    wallet,
                    voucherDiscount,
                    WalletTransactionType.BookingIncome,
                    booking,
                    cancellationToken,
                    countAsEarning: true
                );
            }
        }

        public async Task<OperationResult> HoldCashPlatformFeeAsync(
            BookingEntity booking,
            Guid workerUserId,
            CancellationToken cancellationToken = default
        )
        {
            if (booking.HeldPlatformFee > 0)
            {
                return OperationResult.Success("Đã tạm giữ phí nền tảng cho đơn này");
            }

            var servicePrice = await GetServicePriceBeforeVoucherAsync(booking, cancellationToken);
            var required = PlatformFeeCalculator.RequiredCashDeposit(servicePrice, _settings);
            var wallet = await _walletRepository.GetByUserIdAsync(
                workerUserId,
                WalletOwnerType.Worker,
                cancellationToken
            );
            var balance = wallet?.Balance ?? 0;

            if (wallet == null || balance < required)
            {
                var shortfall = required - balance;
                static string Vnd(long v) => v.ToString("N0", CultureInfo.InvariantCulture).Replace(",", ".");
                var result = OperationResult.Failure(
                    $"Số dư ví chưa đủ để nhận đơn tiền mặt. Cần tối thiểu {Vnd(required)}đ "
                        + $"({(_settings.CashDepositRate * 100).ToString("0", CultureInfo.InvariantCulture)}% giá đơn), hiện có {Vnd(balance)}đ. "
                        + $"Vui lòng nạp thêm {Vnd(shortfall)}đ."
                );
                result.Data = new CashDepositShortfallDto
                {
                    RequiredBalance = required,
                    CurrentBalance = balance,
                    Shortfall = shortfall,
                };
                return result;
            }

            var hold = PlatformFeeCalculator.Calculate(servicePrice, _settings).TotalFee;
            if (hold > 0)
            {
                await DebitAsync(
                    wallet,
                    hold,
                    WalletTransactionType.PlatformFeeHold,
                    TransactionStatus.Pending,
                    booking,
                    cancellationToken,
                    platformFee: hold
                );
            }

            booking.HeldPlatformFee = hold;
            return OperationResult.Success("Đã tạm giữ phí nền tảng");
        }

        public async Task ReleaseCashPlatformFeeAsync(
            BookingEntity booking,
            Guid workerUserId,
            CancellationToken cancellationToken = default
        )
        {
            if (!(booking.HeldPlatformFee > 0))
            {
                return;
            }

            var wallet = await _walletRepository.GetByUserIdAsync(
                workerUserId,
                WalletOwnerType.Worker,
                cancellationToken
            );
            if (wallet == null)
            {
                _logger.LogWarning("Booking {BookingId}: không tìm thấy ví KTV để hoàn khoản giữ phí", booking.Id);
                return;
            }

            var holdTx = await FindPendingHoldAsync(booking, cancellationToken);
            if (holdTx != null)
            {
                holdTx.Status = TransactionStatus.Reversed;
                _walletTransactionRepository.Update(holdTx);
            }

            await CreditAsync(
                wallet,
                booking.HeldPlatformFee.Value,
                WalletTransactionType.PlatformFeeRelease,
                booking,
                cancellationToken
            );

            booking.HeldPlatformFee = 0;
        }

        // =========================================================
        // HELPERS
        // =========================================================

        private async Task<long> GetVoucherDiscountAsync(
            BookingEntity booking,
            CancellationToken cancellationToken
        )
        {
            if (booking.BookingVoucher != null)
            {
                return booking.BookingVoucher.DiscountAmount;
            }

            var bookingVoucher = await _bookingVoucherRepository.GetByBookingIdAsync(
                booking.Id,
                cancellationToken
            );
            return bookingVoucher?.DiscountAmount ?? 0;
        }

        private async Task<WalletTransaction?> FindPendingHoldAsync(
            BookingEntity booking,
            CancellationToken cancellationToken
        )
        {
            var reference = booking.Id.ToString();
            return await _walletTransactionRepository.FirstOrDefaultAsync(
                x =>
                    x.ReferenceId == reference
                    && x.Type == WalletTransactionType.PlatformFeeHold
                    && x.Status == TransactionStatus.Pending,
                cancellationToken
            );
        }

        private async Task<WalletTransaction> DebitAsync(
            Wallet wallet,
            long amount,
            WalletTransactionType type,
            TransactionStatus status,
            BookingEntity booking,
            CancellationToken cancellationToken,
            long? platformFee = null
        )
        {
            var before = wallet.Balance;
            wallet.Balance -= amount;
            wallet.UpdatedAt = DateTime.UtcNow;

            var tx = new WalletTransaction
            {
                WalletId = wallet.Id,
                PaymentOrderId = booking.PaymentOrder?.Id,
                Type = type,
                Direction = WalletDirection.Debit,
                Amount = amount,
                PlatformFee = platformFee,
                BalanceBefore = before,
                BalanceAfter = wallet.Balance,
                ReferenceId = booking.Id.ToString(),
                Status = status,
            };

            await _walletTransactionRepository.AddAsync(tx, cancellationToken);
            _walletRepository.Update(wallet);
            return tx;
        }

        private async Task<WalletTransaction> CreditAsync(
            Wallet wallet,
            long amount,
            WalletTransactionType type,
            BookingEntity booking,
            CancellationToken cancellationToken,
            long? platformFee = null,
            bool countAsEarning = false
        )
        {
            var before = wallet.Balance;
            wallet.Balance += amount;
            if (countAsEarning)
            {
                wallet.LifetimeEarned += amount;
            }
            wallet.UpdatedAt = DateTime.UtcNow;

            var tx = new WalletTransaction
            {
                WalletId = wallet.Id,
                PaymentOrderId = booking.PaymentOrder?.Id,
                Type = type,
                Direction = WalletDirection.Credit,
                Amount = amount,
                PlatformFee = platformFee,
                BalanceBefore = before,
                BalanceAfter = wallet.Balance,
                ReferenceId = booking.Id.ToString(),
                Status = TransactionStatus.Success,
            };

            await _walletTransactionRepository.AddAsync(tx, cancellationToken);
            _walletRepository.Update(wallet);
            return tx;
        }
    }
}
