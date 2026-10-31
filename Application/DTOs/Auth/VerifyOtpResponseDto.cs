namespace Application.DTOs.Auth
{
    public class VerifyOtpResponseDto
    {
        // Gửi lại trong /api/auth/register hoặc /api/auth/reset-password; dùng một lần
        public string VerifyToken { get; set; } = default!;

        public int ExpiresInSeconds { get; set; }
    }
}
