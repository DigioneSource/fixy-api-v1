using Application.Common;
using Application.DTOs.WorkerProfile;
using Application.Interfaces.Repositories;
using Domain.Entity;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class WorkerProfileRepository : Repository<WorkerProfile>, IWorkerProfileRepository
    {
        public WorkerProfileRepository(AppDbContext context)
            : base(context) { }

        public async Task<(List<WorkerProfile>, int)> GetWorkerProfilesAsync(
            WorkerProfileQuery query,
            CancellationToken cancellationToken
        )
        {
            var queryDb = _dbSet
                .Include(x => x.User)
                .Include(x => x.Address)
                .Include(x => x.Services)
                    .ThenInclude(s => s.Category)
                .Include(x => x.Services)
                    .ThenInclude(s => s.Options)
                .AsNoTracking();

            // Filter category
            if (query.CategoryId != null)
            {
                queryDb = queryDb.Where(x => x.Services.Any(s => s.CategoryId == query.CategoryId));
            }

            // Filter status
            if (query.Status != null)
            {
                queryDb = queryDb.Where(x => x.Status == query.Status);
            }

            // Search
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var keyword = query.SearchTerm.Trim().ToLower();

                queryDb = queryDb.Where(x =>
                    (x.Bio != null && x.Bio.ToLower().Contains(keyword))
                    || (x.User != null && x.User.FullName.ToLower().Contains(keyword))
                );
            }

            // Sort
            queryDb = query.SortBy?.ToLower() switch
            {
                "name" => query.SortDescending
                    ? queryDb.OrderByDescending(x => x.User!.FullName)
                    : queryDb.OrderBy(x => x.User!.FullName),

                "yearexperiences" => query.SortDescending
                    ? queryDb.OrderByDescending(x => x.ExperienceYears)
                    : queryDb.OrderBy(x => x.ExperienceYears),

                "rating" => query.SortDescending
                    ? queryDb.OrderByDescending(x => x.RatingAvg)
                    : queryDb.OrderBy(x => x.RatingAvg),

                "totalreviews" => query.SortDescending
                    ? queryDb.OrderByDescending(x => x.TotalReviews)
                    : queryDb.OrderBy(x => x.TotalReviews),

                "createddate" => query.SortDescending
                    ? queryDb.OrderByDescending(x => x.CreatedDate)
                    : queryDb.OrderBy(x => x.CreatedDate),

                _ => queryDb.OrderByDescending(x => x.CreatedDate),
            };

            var totalCount = await queryDb.CountAsync(cancellationToken);

            var items = await queryDb
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(cancellationToken);

            return (items, totalCount);
        }

        public async Task<WorkerProfile?> GetWorkerProfileByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default
        )
        {
            return await _dbSet
                .Include(x => x.User)
                .Include(x => x.Address)
                .FirstOrDefaultAsync(x => x.UserId == userId || x.Id == userId, cancellationToken);
        }

        public async Task<WorkerProfile?> GetWorkerProfileDetailByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default
        )
        {
            return await _dbSet
                .Include(x => x.User)
                // Worker Address
                .Include(x => x.Address)
                // Certificates
                .Include(x => x.Certificates)
                // Services
                .Include(x => x.Services)
                    .ThenInclude(s => s.Category)
                .Include(x => x.Services)
                    .ThenInclude(s => s.Options)
                // Reviews
                .Include(x => x.Reviews)
                // Schedule
                .Include(x => x.WeeklySchedules)
                .Include(x => x.ScheduleExceptions)
                .FirstOrDefaultAsync(x => x.UserId == userId || x.Id == userId, cancellationToken);
        }

        public async Task<WorkerProfile?> GetDetailByIdAsync(
            Guid workerProfileId,
            CancellationToken cancellationToken = default
        )
        {
            return await _dbSet
                .Include(x => x.User)
                .Include(x => x.Address)
                .Include(x => x.Certificates)
                .Include(x => x.Services)
                    .ThenInclude(s => s.Category)
                .Include(x => x.Services)
                    .ThenInclude(s => s.Options)
                .Include(x => x.Reviews)
                .FirstOrDefaultAsync(x => x.Id == workerProfileId || x.UserId == workerProfileId, cancellationToken);
        }

        public async Task<bool> IsApprovedWorkerAsync(
            Guid workerProfileId,
            CancellationToken cancellationToken = default
        )
        {
            return await _dbSet.AnyAsync(
                x => x.Id == workerProfileId && x.Status == Domain.Enum.WorkerStatus.Approved,
                cancellationToken
            );
        }

        public async Task<(
            List<WorkerProfile> Items,
            List<double?> Distances,
            int TotalCount
        )> SearchWorkersForCustomerAsync(
            CustomerWorkerSearchQuery query,
            CancellationToken cancellationToken
        )
        {
            // 1. Base query: chỉ thợ đã duyệt, đã đóng cọc ký quỹ, đang bật nhận việc và tài khoản hoạt động
            var queryDb = _dbSet
                .Include(x => x.User)
                .Include(x => x.Address)
                .Include(x => x.Services)
                    .ThenInclude(s => s.Category)
                .Include(x => x.Services)
                    .ThenInclude(s => s.Options)
                .AsNoTracking()
                .Where(x =>
                    x.Status == Domain.Enum.WorkerStatus.Approved
                    // 21/11/2026 CEO: bỏ cọc kích hoạt — đơn tiền mặt đã có số dư bảo đảm 40%.
                    && x.IsAcceptingJobs
                    && x.User != null
                    && x.User.IsActive
                    && !x.User.IsDeleted
                );

            // 2. Lọc theo danh mục (hỗ trợ phân cấp bằng Code)
            if (query.CategoryId.HasValue)
            {
                var targetCategory = await _context
                    .Set<ServiceCategory>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == query.CategoryId.Value, cancellationToken);

                if (targetCategory != null)
                {
                    var categoryCode = targetCategory.Code;
                    var codePrefix = categoryCode + ".";

                    queryDb = queryDb.Where(x =>
                        x.Services.Any(s =>
                            s.Category != null
                            && (
                                s.Category.Code == categoryCode
                                || s.Category.Code.StartsWith(codePrefix)
                            )
                        )
                    );
                }
            }

            // 3. Tìm kiếm theo từ khóa tự do
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var keyword = query.SearchTerm.Trim().ToLower();
                queryDb = queryDb.Where(x =>
                    x.User!.FullName.ToLower().Contains(keyword)
                    || (x.Bio != null && x.Bio.ToLower().Contains(keyword))
                    || x.Services.Any(s =>
                        s.Category != null && s.Category.Name.ToLower().Contains(keyword)
                    )
                );
            }

            // 4. Lọc theo trạng thái Online
            if (query.IsOnline.HasValue)
            {
                queryDb = queryDb.Where(x => x.IsOnline == query.IsOnline.Value);
            }

            // 5. Lọc theo điểm đánh giá
            if (query.MinRating.HasValue)
            {
                queryDb = queryDb.Where(x => x.RatingAvg >= query.MinRating.Value);
            }

            // 6. Lọc theo khu vực hành chính
            if (!string.IsNullOrWhiteSpace(query.City))
            {
                var city = query.City.Trim().ToLower();
                queryDb = queryDb.Where(x =>
                    x.Address != null && x.Address.City.ToLower().Contains(city)
                );
            }

            if (!string.IsNullOrWhiteSpace(query.Ward))
            {
                var ward = query.Ward.Trim().ToLower();
                queryDb = queryDb.Where(x =>
                    x.Address != null && x.Address.Ward.ToLower().Contains(ward)
                );
            }

            // 7. Lọc theo khoảng giá
            if (query.MinPrice.HasValue || query.MaxPrice.HasValue)
            {
                queryDb = queryDb.Where(x =>
                    x.Services.Any(s =>
                        (!query.CategoryId.HasValue || s.CategoryId == query.CategoryId.Value)
                        && (query.CategoryId.HasValue || s.IsPrimary)
                        && (!query.MinPrice.HasValue || s.BasePrice >= query.MinPrice.Value)
                        && (!query.MaxPrice.HasValue || s.BasePrice <= query.MaxPrice.Value)
                    )
                );
            }

            // 8. Projection với tính toán khoảng cách Haversine
            var hasCoordinates = query.CustomerLat.HasValue && query.CustomerLng.HasValue;
            var lat1Rad = hasCoordinates ? query.CustomerLat!.Value * Math.PI / 180.0 : 0.0;
            var lng1Rad = hasCoordinates ? query.CustomerLng!.Value * Math.PI / 180.0 : 0.0;
            const double EarthRadiusKm = 6371.0;
            const double Deg2Rad = Math.PI / 180.0;

            var projectedQuery = queryDb
                .Select(x => new
                {
                    Worker = x,
                    EffectiveLat = x.CurrentLat
                        ?? (x.Address != null ? x.Address.Lat : (double?)null),
                    EffectiveLng = x.CurrentLng
                        ?? (x.Address != null ? x.Address.Lng : (double?)null),
                })
                .Select(x => new
                {
                    x.Worker,
                    DistanceKm = (
                        !hasCoordinates || x.EffectiveLat == null || x.EffectiveLng == null
                    )
                        ? (double?)null
                        : EarthRadiusKm
                            * Math.Acos(
                                (
                                    Math.Sin(lat1Rad) * Math.Sin(x.EffectiveLat.Value * Deg2Rad)
                                    + Math.Cos(lat1Rad)
                                        * Math.Cos(x.EffectiveLat.Value * Deg2Rad)
                                        * Math.Cos(x.EffectiveLng.Value * Deg2Rad - lng1Rad)
                                ) > 1.0
                                    ? 1.0
                                    : (
                                        Math.Sin(lat1Rad) * Math.Sin(x.EffectiveLat.Value * Deg2Rad)
                                        + Math.Cos(lat1Rad)
                                            * Math.Cos(x.EffectiveLat.Value * Deg2Rad)
                                            * Math.Cos(x.EffectiveLng.Value * Deg2Rad - lng1Rad)
                                    )
                            ),
                });

            // 9. Lọc theo bán kính
            if (hasCoordinates && query.RadiusKm.HasValue)
            {
                projectedQuery = projectedQuery.Where(x =>
                    x.DistanceKm != null && x.DistanceKm <= query.RadiusKm.Value
                );
            }

            // 10. Truy vấn động biên giá dịch vụ (Dynamic Min/Max Price Bounds)
            var activeServicesQuery = queryDb
                .SelectMany(x => x.Services)
                .Where(s =>
                    (!query.CategoryId.HasValue || s.CategoryId == query.CategoryId.Value)
                    && (query.CategoryId.HasValue || s.IsPrimary)
                );

            double minPriceBound = RankingConstants.FallbackMinPrice;
            double maxPriceBound = RankingConstants.FallbackMaxPrice;

            if (await activeServicesQuery.AnyAsync(cancellationToken))
            {
                minPriceBound = (double)await activeServicesQuery.MinAsync(
                    s => s.BasePrice,
                    cancellationToken
                );
                maxPriceBound = (double)await activeServicesQuery.MaxAsync(
                    s => s.BasePrice,
                    cancellationToken
                );
            }

            // Tránh chia cho 0 nếu tất cả dịch vụ có cùng một giá
            if (Math.Abs(maxPriceBound - minPriceBound) < 0.01)
            {
                maxPriceBound = minPriceBound + 10000.0;
            }

            // 11. Xác định bộ trọng số theo kiểu sắp xếp
            var sortBy = query.SortBy?.ToLower();
            bool isSortPriceDescending = query.SortDescending;

            double wRating = RankingConstants.DefRatingWeight;
            double wPrice = RankingConstants.DefPriceWeight;
            double wDistance = RankingConstants.DefDistanceWeight;

            if (sortBy == "price")
            {
                wRating = RankingConstants.PricePriorityRatingWeight;
                wPrice = RankingConstants.PricePriorityPriceWeight;
                wDistance = RankingConstants.PricePriorityDistanceWeight;
            }
            else if (sortBy == "nearest" && hasCoordinates)
            {
                wRating = RankingConstants.DistPriorityRatingWeight;
                wPrice = RankingConstants.DistPriorityPriceWeight;
                wDistance = RankingConstants.DistPriorityDistanceWeight;
            }

            double maxRadiusBound = query.RadiusKm ?? RankingConstants.DefaultMaxRadiusKm;

            // 12. Chiếu dữ liệu với ServicePrice
            var typedQuery = projectedQuery.Select(x => new WorkerSearchProjection
            {
                Worker = x.Worker,
                DistanceKm = x.DistanceKm,
                ServicePrice = (double)(x.Worker.Services
                    .Where(s =>
                        (!query.CategoryId.HasValue || s.CategoryId == query.CategoryId.Value)
                        && (query.CategoryId.HasValue || s.IsPrimary)
                    )
                    .Select(s => s.BasePrice)
                    .FirstOrDefault()),
            });

            // 13. Tính điểm xếp hạng tổng hợp (Weighted Ranking Score)
            var scoredQuery = typedQuery.Select(x => new WorkerSearchProjection
            {
                Worker = x.Worker,
                DistanceKm = x.DistanceKm,
                ServicePrice = x.ServicePrice,
                RankingScore =
                    // A. Rating Component: RatingAvg / 5.0 (chuẩn hóa về [0, 1])
                    (wRating * (x.Worker.RatingAvg / 5.0))
                    // B. Price Component: chuẩn hóa giá theo biên động
                    + (wPrice
                        * (
                            isSortPriceDescending
                                ? ((x.ServicePrice - minPriceBound)
                                    / (maxPriceBound - minPriceBound))
                                : (1.0
                                    - ((x.ServicePrice - minPriceBound)
                                        / (maxPriceBound - minPriceBound)))
                        ))
                    // C. Distance Component: chuẩn hóa khoảng cách
                    + (wDistance
                        * (
                            x.DistanceKm.HasValue
                                ? (1.0 - (x.DistanceKm.Value / maxRadiusBound))
                                : 0.5
                        )),
            });

            // 14. Sắp xếp theo điểm tổng hợp hoặc thuộc tính được chọn
            IOrderedQueryable<WorkerSearchProjection> orderedQuery;
            if (sortBy == "popular")
            {
                orderedQuery = scoredQuery.OrderByDescending(x => x.Worker.TotalOrders)
                                          .ThenByDescending(x => x.Worker.TotalReviews)
                                          .ThenByDescending(x => x.RankingScore);
            }
            else
            {
                orderedQuery = scoredQuery.OrderByDescending(x => x.RankingScore);
            }

            var totalCount = await typedQuery.CountAsync(cancellationToken);

            var pagedItems = await orderedQuery
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(cancellationToken);

            var items = pagedItems.Select(x => x.Worker).ToList();
            var distances = pagedItems.Select(x => x.DistanceKm).ToList();

            return (items, distances, totalCount);
        }

        /// <summary>
        /// Helper class for projecting worker search results with calculated distance,
        /// service price, and weighted ranking score.
        /// </summary>
        private class WorkerSearchProjection
        {
            public WorkerProfile Worker { get; set; } = null!;
            public double? DistanceKm { get; set; }
            public double ServicePrice { get; set; }
            public double RankingScore { get; set; }
        }
    }
}
