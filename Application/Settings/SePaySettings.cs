namespace Application.Settings
{
    /// <summary>
    /// Thu tiền bằng chuyển khoản VietQR, SePay báo giao dịch vào qua webhook (21/11/2026).
    /// Section "SePay" — dùng chung WebhookToken với webhook chi trả KTV.
    /// </summary>
    public class SePaySettings
    {
        /// <summary>Mã ngân hàng theo qr.sepay.vn, ví dụ "MBBank", "Vietcombank", "Agribank".</summary>
        public string BankCode { get; set; } = string.Empty;

        public string AccountNumber { get; set; } = string.Empty;

        /// <summary>Tên chủ tài khoản (in hoa, không dấu) — chỉ để hiển thị cho khách.</summary>
        public string AccountName { get; set; } = string.Empty;

        /// <summary>Token SePay gửi trong header "Authorization: Apikey ...". BẮT BUỘC cho tiền vào.</summary>
        public string WebhookToken { get; set; } = string.Empty;

        /// <summary>Tiền tố nội dung chuyển khoản để nhận ra đơn của TAYZEN.</summary>
        public string CodePrefix { get; set; } = "TZ";

        /// <summary>
        /// Chuỗi ngân hàng bắt buộc đứng đầu nội dung để SePay nhận được giao dịch.
        /// VietinBank cá nhân / hộ kinh doanh: "SEVQR" (thiếu là SePay KHÔNG thấy giao dịch — 21/11/2026).
        /// </summary>
        public string ContentPrefix { get; set; } = "SEVQR";

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(BankCode) && !string.IsNullOrWhiteSpace(AccountNumber);
    }
}
