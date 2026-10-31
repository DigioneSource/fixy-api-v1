namespace Application.DTOs.BookingDraft
{
    public class ConfirmBookingDraftRequest
    {
        /// <summary>
        /// Khách hàng xác nhận đồng ý với Hợp đồng dịch vụ điện tử và điều khoản sử dụng
        /// </summary>
        // Mặc định false: client bắt buộc gửi acceptedTerms = true.
        public bool AcceptedTerms { get; set; } = false;
    }
}
