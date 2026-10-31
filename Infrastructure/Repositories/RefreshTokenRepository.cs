using Application.Interfaces.Repositories;
using Domain.Entity;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Org.BouncyCastle.Asn1;

namespace Infrastructure.Persistence.Repositories
{
    public class RefreshTokenRepository : Repository<RefreshToken>, IRefreshTokenRepository
    {
        public RefreshTokenRepository(AppDbContext context)
            : base(context) { }

        public async Task<RefreshToken?> GetByTokenHashAsync(
            string tokenHash,
            CancellationToken ct = default
        )
        {
            return await _dbSet.FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct);
        }

        public async Task<RefreshToken?> GetValidTokenWithUserAsync(
            string tokenHash,
            CancellationToken ct
        )
        {
            return await _dbSet
                .Include(x => x.User!)
                    .ThenInclude(x => x.UserRoles)
                        .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.TokenHash == tokenHash && !x.IsRevoked, ct);
        }

        public async Task<List<RefreshToken>> GetActiveByUserIdAsync(
            Guid userId,
            CancellationToken ct = default
        )
        {
            return await _dbSet.Where(x => x.UserId == userId && !x.IsRevoked).ToListAsync(ct);
        }
    }
}
