using System.Text.Json;
using Application.Common;
using Application.Common.Interfaces;
using Application.DTOs.Booking;
using Application.DTOs.Worker;
using Application.Interfaces.Hubs;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services.Worker;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Worker
{
    public class WorkerLocationService : IWorkerLocationService
    {
        private readonly IDistributedCache _cache;
        private readonly IBookingRepository _bookingRepository;
        private readonly IBookingHubService _bookingHubService;
        private readonly ICurrentUserService _currentUserService;
        private readonly IWorkerProfileRepository _workerProfileRepository;
        private readonly ILogger<WorkerLocationService> _logger;

        private static readonly TimeSpan LocationTtl = TimeSpan.FromHours(2);

        public WorkerLocationService(
            IDistributedCache cache,
            IBookingRepository bookingRepository,
            IBookingHubService bookingHubService,
            ICurrentUserService currentUserService,
            IWorkerProfileRepository workerProfileRepository,
            ILogger<WorkerLocationService> logger
        )
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));

            _bookingRepository =
                bookingRepository ?? throw new ArgumentNullException(nameof(bookingRepository));

            _bookingHubService =
                bookingHubService ?? throw new ArgumentNullException(nameof(bookingHubService));

            _currentUserService =
                currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));

            _workerProfileRepository =
                workerProfileRepository
                ?? throw new ArgumentNullException(nameof(workerProfileRepository));

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<OperationResult> UpdateLocationAsync(
            Guid userId,
            UpdateWorkerLocationRequest request,
            CancellationToken cancellationToken = default
        )
        {
            // FIX:
            // userId = User.Id
            // cần resolve sang WorkerProfile.Id

            var workerProfile = await _workerProfileRepository.GetWorkerProfileByUserIdAsync(
                userId,
                cancellationToken
            );

            if (workerProfile == null)
            {
                return OperationResult.Failure("Worker profile not found");
            }

            var workerProfileId = workerProfile.Id;

            var locationData = new WorkerLocationUpdateDto
            {
                WorkerId = workerProfileId,
                Lat = request.Lat,
                Lng = request.Lng,
                UpdatedAt = DateTime.UtcNow,
            };

            await SaveToRedisAsync(workerProfileId, locationData, cancellationToken);

            var activeBooking = await _bookingRepository.GetActiveBookingByWorkerProfileIdAsync(
                workerProfileId,
                cancellationToken
            );

            if (activeBooking == null)
            {
                _logger.LogDebug(
                    "WorkerProfile {WorkerProfileId} updated location but has no active booking.",
                    workerProfileId
                );

                return OperationResult.Success("Location updated successfully");
            }

            locationData.BookingId = activeBooking.Id;

            await _bookingHubService.SendLocationUpdateAsync(
                activeBooking.Id,
                locationData,
                cancellationToken
            );

            _logger.LogInformation(
                "WorkerProfile {WorkerProfileId} broadcasted location to Booking {BookingId}",
                workerProfileId,
                activeBooking.Id
            );

            return OperationResult.Success("Location updated and broadcast successfully");
        }

        public async Task<WorkerLocationUpdateDto?> GetLastLocationAsync(
            Guid workerProfileId,
            CancellationToken cancellationToken = default
        )
        {
            var key = BuildLocationKey(workerProfileId);

            var payload = await _cache.GetStringAsync(key, cancellationToken);

            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            return JsonSerializer.Deserialize<WorkerLocationUpdateDto>(payload);
        }

        public async Task<OperationResult<BookingTrackingDto>> GetBookingTrackingAsync(
            Guid bookingId,
            CancellationToken cancellationToken = default
        )
        {
            var booking = await _bookingRepository.GetBookingWithWorkerAsync(
                bookingId,
                cancellationToken
            );

            if (booking == null)
            {
                return OperationResult<BookingTrackingDto>.Failure("Booking not found");
            }

            // Vị trí KTV chỉ khách của chính đơn này được xem (an toàn cho KTV, chống IDOR).
            if (
                !Guid.TryParse(_currentUserService.UserId, out var requesterId)
                || booking.CustomerProfile?.UserId != requesterId
            )
            {
                return OperationResult<BookingTrackingDto>.Failure("Booking not found");
            }

            var trackingDto = new BookingTrackingDto
            {
                BookingId = booking.Id,
                Status = booking.Status.ToString(),
            };

            // FIX:
            // booking.WorkerProfile
            // KHÔNG dùng booking.Worker

            if (booking.WorkerProfile != null && booking.WorkerProfile.User != null)
            {
                trackingDto.WorkerInfo = new WorkerTrackingInfoDto
                {
                    WorkerId = booking.WorkerProfile.Id,

                    FullName = booking.WorkerProfile.User.FullName,

                    // CEO 31/10: không trả số điện thoại KTV cho khách — gọi qua ứng dụng.
                    Phone = null,

                    RatingAvg = booking.WorkerProfile.RatingAvg,
                };
            }

            // FIX:
            // Booking.WorkerProfileId

            if (booking.WorkerProfileId.HasValue)
            {
                var lastLocation = await GetLastLocationAsync(
                    booking.WorkerProfileId.Value,
                    cancellationToken
                );

                if (lastLocation != null)
                {
                    trackingDto.WorkerLat = lastLocation.Lat;

                    trackingDto.WorkerLng = lastLocation.Lng;

                    trackingDto.LocationUpdatedAt = lastLocation.UpdatedAt;
                }
            }

            return OperationResult<BookingTrackingDto>.Success(
                trackingDto,
                "Tracking information retrieved successfully"
            );
        }

        // =========================
        // Private Helpers
        // =========================

        private async Task SaveToRedisAsync(
            Guid workerProfileId,
            WorkerLocationUpdateDto data,
            CancellationToken cancellationToken
        )
        {
            var key = BuildLocationKey(workerProfileId);

            var payload = JsonSerializer.Serialize(data);

            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = LocationTtl,
            };

            await _cache.SetStringAsync(key, payload, options, cancellationToken);
        }

        // worker:{workerProfileId}:location
        private static string BuildLocationKey(Guid workerProfileId)
        {
            return $"worker:{workerProfileId}:location";
        }
    }
}
