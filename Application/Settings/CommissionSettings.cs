namespace Application.Settings
{
    /// <summary>
    /// Tỉ lệ phí nền tảng — CEO chốt 31/10/2026 (mô hình trung gian: TAYZEN thu hộ, chỉ phí là doanh thu).
    /// Đọc từ cấu hình mục "Commission". Khớp hằng số phía app: features/finance/commission.ts.
    /// </summary>
    public class CommissionSettings
    {
        public const string SectionName = "Commission";

        /// <summary>Phí nền tảng chưa thuế, tính trên giá dịch vụ trước voucher.</summary>
        public decimal PlatformFeeRate { get; set; } = 0.30m;

        /// <summary>Thuế GTGT trên phí nền tảng.</summary>
        public decimal VatRate { get; set; } = 0.10m;

        /// <summary>Số dư ví KTV tối thiểu (theo giá đơn) để nhận đơn tiền mặt.</summary>
        public decimal CashDepositRate { get; set; } = 0.40m;

        /// <summary>Phí huỷ khi khách huỷ từ lúc KTV xuất phát, tính trên giá dịch vụ.</summary>
        public decimal CancellationFeeRate { get; set; } = 0.30m;

        /// <summary>Phần phí huỷ chia cho KTV (bù chi phí đi lại); phần còn lại cho nền tảng.</summary>
        public decimal CancellationWorkerShareRate { get; set; } = 0.70m;
    }
}
