using Application.Interfaces.Repositories;
using Domain.Entity;
using Domain.Enum;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories
{
    public class UserOtpRepository : Repository<UserOtp>, IUserOtpRepository
    {
        public UserOtpRepository(AppDbContext context)
            : base(context) { }

        public async Task<UserOtp?> GetLatestByTargetAsync(
            string target,
            CancellationToken ct = default
        )
        {
            return await _dbSet
                .Where(x => x.Target == target)
                .OrderByDescending(x => x.CreatedDate)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<List<UserOtp>> GetUnusedOtpsAsync(
            string target,
            CancellationToken ct = default
        )
        {
            return await _dbSet.Where(x => x.Target == target && !x.IsUsed).ToListAsync(ct);
        }

        public async Task<UserOtp?> GetLatestPendingOtpAsync(
            string target,
            CancellationToken ct = default
        )
        {
            return await _dbSet
                .Where(x => x.Target == target && !x.IsUsed && !x.IsVerified)
                .OrderByDescending(x => x.CreatedDate)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<UserOtp?> GetConsumableVerifiedOtpAsync(
            string target,
            EmailPurpose purpose,
            string verifyTokenHash,
            DateTime verifiedAfter,
            CancellationToken ct = default
        )
        {
            return await _dbSet
                .Where(x =>
                    x.Target == target
                    && x.Purpose == purpose
                    && x.IsVerified
                    && x.ConsumedAt == null
                    && x.VerifyTokenHash == verifyTokenHash
                    && x.VerifiedAt != null
                    && x.VerifiedAt > verifiedAfter
                )
                .OrderByDescending(x => x.CreatedDate)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<UserOtp?> GetLatestOtpByTargetAsync(string target, CancellationToken ct)
        {
            return await _dbSet
                .Where(x => x.Target == target)
                .OrderByDescending(x => x.CreatedDate)
                .FirstOrDefaultAsync(ct);
        }
    }
}
