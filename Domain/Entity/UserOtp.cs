using Domain.Common;
using Domain.Enum;

namespace Domain.Entity
{
    public class UserOtp : BaseEntity
    {
        public string Target { get; set; } = default!;

        // phone number OR email

        public string OtpCode { get; set; } = default!;

        public EmailPurpose Purpose { get; set; }

        public DateTime ExpiryDate { get; set; }

        public bool IsUsed { get; set; }

        public bool IsVerified { get; set; }

        // Số lần nhập sai mã; đủ ngưỡng thì mã bị vô hiệu
        public int FailedAttempts { get; set; }

        // SHA-256 của verifyToken trả cho client sau khi xác thực đúng mã
        public string? VerifyTokenHash { get; set; }

        public DateTime? VerifiedAt { get; set; }

        // Thời điểm verifyToken đã được dùng (đăng ký / đặt lại mật khẩu)
        public DateTime? ConsumedAt { get; set; }
    }
}
