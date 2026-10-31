using Domain.Entity;

namespace Application.Interfaces.Repositories
{
    public interface IRefreshTokenRepository : IRepository<RefreshToken>
    {
        Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);

        Task<RefreshToken?> GetValidTokenWithUserAsync(
            string tokenHash,
            CancellationToken ct = default
        );

        Task<List<RefreshToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken ct = default);
    }
}
