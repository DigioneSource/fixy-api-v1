using Domain.Entity;
using Domain.Enum;

namespace Application.Interfaces.Repositories
{
    public interface IUserOtpRepository : IRepository<UserOtp>
    {
        Task<UserOtp?> GetLatestByTargetAsync(string target, CancellationToken ct = default);

        Task<List<UserOtp>> GetUnusedOtpsAsync(string target, CancellationToken ct = default);

        // OTP mới nhất chưa dùng, chưa xác thực của target (không lọc theo mã để đếm lần nhập sai)
        Task<UserOtp?> GetLatestPendingOtpAsync(string target, CancellationToken ct = default);

        // OTP đã xác thực, chưa tiêu thụ, đúng mục đích, khớp hash verifyToken và còn hạn
        Task<UserOtp?> GetConsumableVerifiedOtpAsync(
            string target,
            EmailPurpose purpose,
            string verifyTokenHash,
            DateTime verifiedAfter,
            CancellationToken ct = default
        );

        Task<UserOtp?> GetLatestOtpByTargetAsync(string target, CancellationToken ct);
    }
}
