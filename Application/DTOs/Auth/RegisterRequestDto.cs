using Domain.Enum;

namespace Application.DTOs.Auth
{
    public class RegisterRequestDto
    {
        public string FullName { get; set; } = default!;

        public string Password { get; set; } = default!;

        public string Target { get; set; } = default!;
        public RoleRegister RoleRegister { get; set; }

        // Nhận từ data.verifyToken của /api/auth/otp/verify (Purpose = Register)
        public string VerifyToken { get; set; } = default!;
    }
}
