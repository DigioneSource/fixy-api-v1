namespace Application.Interfaces.Services.Sms
{
    public interface ISmsService
    {
        Task<bool> SendSmsAsync(string phoneNumber, string message, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gửi mã OTP. Provider "ZnsRelay" gửi qua Zalo ZNS (trạm của ERP DIGIFNB, chỉ cần mã);
        /// provider khác gửi SMS với nội dung <paramref name="message"/>.
        /// </summary>
        Task<bool> SendOtpAsync(string phoneNumber, string otpCode, string message, CancellationToken cancellationToken = default);
    }
}
