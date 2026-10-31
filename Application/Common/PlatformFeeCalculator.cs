using Application.Settings;
using Domain.Enum;

namespace Application.Common
{
    /// <summary>Phí nền tảng của một đơn (VND).</summary>
    public readonly record struct PlatformFeeBreakdown(
        long ServicePrice,
        long PlatformFee,
        long PlatformFeeVat,
        long WorkerNet
    )
    {
        /// <summary>Tổng KTV trả nền tảng (phí + VAT).</summary>
        public long TotalFee => PlatformFee + PlatformFeeVat;
    }

    /// <summary>Phí huỷ của một đơn (VND).</summary>
    public readonly record struct CancellationFeeBreakdown(
        long CancellationFee,
        long WorkerShare,
        long PlatformShare,
        long RefundAmount
    );

    /// <summary>
    /// Công thức tiền thuần (không phụ thuộc CSDL) — CEO chốt 31/10/2026.
    /// Làm tròn như Math.round của app (nửa lên) để số BE và số tạm tính của app khớp nhau.
    /// </summary>
    public static class PlatformFeeCalculator
    {
        private static long Round(decimal value) =>
            (long)Math.Round(value, 0, MidpointRounding.AwayFromZero);

        /// <summary>
        /// PlatformFee = round(phí% × giá trước voucher); PlatformFeeVat = round(VAT% × PlatformFee);
        /// WorkerNet = giá − PlatformFee − PlatformFeeVat. Voucher do nền tảng chịu, không trừ vào KTV.
        /// </summary>
        public static PlatformFeeBreakdown Calculate(long servicePrice, CommissionSettings settings)
        {
            var price = Math.Max(0, servicePrice);
            var fee = Round(price * settings.PlatformFeeRate);
            var vat = Round(fee * settings.VatRate);
            return new PlatformFeeBreakdown(price, fee, vat, price - fee - vat);
        }

        /// <summary>Số dư ví tối thiểu để KTV nhận đơn tiền mặt: CashDepositRate × giá, làm tròn lên nghìn đồng.</summary>
        public static long RequiredCashDeposit(long servicePrice, CommissionSettings settings)
        {
            var raw = Math.Max(0, servicePrice) * settings.CashDepositRate;
            return (long)(Math.Ceiling(raw / 1000m) * 1000m);
        }

        /// <summary>Khách huỷ từ lúc KTV xuất phát (Traveling, Arrived) thì mất phí huỷ.</summary>
        public static bool IsCancellationFeeApplicable(BookingStatus status) =>
            status == BookingStatus.Traveling || status == BookingStatus.Arrived;

        /// <summary>
        /// Phí huỷ khi khách huỷ:
        /// - Trước Traveling: miễn phí, hoàn đủ số đã trả.
        /// - Traveling/Arrived: phí = round(CancellationFeeRate × giá dịch vụ), tối đa bằng số khách đã trả;
        ///   KTV nhận round(CancellationWorkerShareRate × phí), nền tảng giữ phần còn lại;
        ///   hoàn cho khách = số đã trả − phí.
        /// Đơn chưa trả trước (tiền mặt) thì paidAmount = 0: phí chỉ là số phải thu, không hoàn gì.
        /// </summary>
        public static CancellationFeeBreakdown CalculateCancellation(
            BookingStatus status,
            long servicePrice,
            long paidAmount,
            CommissionSettings settings
        )
        {
            var paid = Math.Max(0, paidAmount);

            if (!IsCancellationFeeApplicable(status))
            {
                return new CancellationFeeBreakdown(0, 0, 0, paid);
            }

            var fee = Round(Math.Max(0, servicePrice) * settings.CancellationFeeRate);
            if (paid > 0)
            {
                fee = Math.Min(fee, paid);
            }

            var workerShare = Round(fee * settings.CancellationWorkerShareRate);
            return new CancellationFeeBreakdown(fee, workerShare, fee - workerShare, Math.Max(0, paid - fee));
        }
    }
}
