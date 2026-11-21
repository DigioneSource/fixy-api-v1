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

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(BankCode) && !string.IsNullOrWhiteSpace(AccountNumber);
    }
}
