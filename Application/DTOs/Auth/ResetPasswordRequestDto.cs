using Domain.Enum;

namespace Application.DTOs.Auth
{
    public class ResetPasswordRequestDto
    {
        public string Target { get; set; } = default!;
        public string NewPassword { get; set; } = default!;

        // Nhận từ data.verifyToken của /api/auth/otp/verify (Purpose = ForgotPassword)
        public string VerifyToken { get; set; } = default!;
    }
}
