namespace Application.DTOs.Booking
{
    /// <summary>
    /// Trả trong <c>data</c> khi KTV nhận đơn tiền mặt mà số dư ví chưa đủ ký quỹ (CEO 31/10).
    /// </summary>
    public class CashDepositShortfallDto
    {
        /// <summary>Số dư tối thiểu cần có = 40% giá đơn, làm tròn lên nghìn đồng.</summary>
        public long RequiredBalance { get; set; }

        /// <summary>Số dư khả dụng hiện tại của ví KTV.</summary>
        public long CurrentBalance { get; set; }

        /// <summary>Số tiền cần nạp thêm.</summary>
        public long Shortfall { get; set; }
    }
}
