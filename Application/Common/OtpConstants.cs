namespace Application.Common
{
    public static class OtpConstants
    {
        // Số lần nhập sai tối đa cho một mã OTP
        public const int MaxFailedAttempts = 5;

        // Thời gian hiệu lực của verifyToken kể từ lúc xác thực OTP thành công
        public const int VerifyTokenLifetimeMinutes = 10;
    }
}
