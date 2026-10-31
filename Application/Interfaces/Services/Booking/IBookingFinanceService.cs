using Application.Common;
using BookingEntity = Domain.Entity.Booking;

namespace Application.Interfaces.Services.Booking
{
    /// <summary>
    /// Tiền của đơn theo quy định CEO 31/10/2026 (phí nền tảng, ký quỹ đơn tiền mặt, phí huỷ).
    /// Các hàm chỉ ghi thay đổi vào DbContext (ví, giao dịch ví, đơn) — KHÔNG SaveChanges,
    /// để người gọi lưu cùng một lần với việc đổi trạng thái đơn (một transaction).
    /// </summary>
    public interface IBookingFinanceService
    {
        /// <summary>Giá dịch vụ trước voucher = FinalPrice + giảm giá voucher (voucher do nền tảng chịu).</summary>
        Task<long> GetServicePriceBeforeVoucherAsync(
            BookingEntity booking,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// Đơn hoàn thành: chốt PlatformFee/PlatformFeeVat/WorkerNet vào đơn và quyết toán ví KTV.
        /// Cần nạp sẵn booking.WorkerProfile và booking.PaymentOrder.
        /// </summary>
        Task SettleCompletedBookingAsync(
            BookingEntity booking,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// KTV nhận đơn tiền mặt: ví phải có ≥ CashDepositRate × giá đơn; đủ thì tạm giữ (phí + VAT).
        /// Thiếu thì trả Failure, Data = { requiredBalance, currentBalance, shortfall }, không đổi gì.
        /// </summary>
        Task<OperationResult> HoldCashPlatformFeeAsync(
            BookingEntity booking,
            Guid workerUserId,
            CancellationToken cancellationToken = default
        );

        /// <summary>Đơn bị huỷ: hoàn khoản tạm giữ phí nền tảng (nếu có) về ví KTV.</summary>
        Task ReleaseCashPlatformFeeAsync(
            BookingEntity booking,
            Guid workerUserId,
            CancellationToken cancellationToken = default
        );
    }
}
