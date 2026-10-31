using Application.Common;
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

        /// <summary>Đơn tiền mặt hoàn thành (bổ sung ở bước ký quỹ).</summary>
        private Task SettleCashAsync(
            BookingEntity booking,
            Wallet wallet,
            PlatformFeeBreakdown breakdown,
            long servicePrice,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

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
