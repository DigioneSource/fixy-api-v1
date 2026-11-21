using Application.Common;
using Application.DTOs.Booking;
using Application.Interfaces;
using Application.Interfaces.Hubs;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Interfaces.Services.Booking;
using Application.Interfaces.Services.Worker;
using Application.Settings;
using Domain.Entity;
using Domain.Enum;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Infrastructure.Services;

namespace Infrastructure.Services.Booking
{
    public class WorkerMatchingService : IWorkerMatchingService
    {
        private readonly IBookingRepository _bookingRepository;
        private readonly IWorkerProfileRepository _workerProfileRepository;
        private readonly IWorkerServiceRepository _workerServiceRepository;
        private readonly IWorkerMatchingQueueRepository _matchingQueueRepository;
        private readonly IWorkerLocationService _workerLocationService;
        private readonly IBookingHubService _bookingHubService;
        private readonly INotificationService _notificationService;
        private readonly IMediaRepository _mediaRepository;
        private readonly IServiceCategoryRepository _serviceCategoryRepository;
        private readonly IWorkerScheduleExceptionService _workerScheduleExceptionService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly WorkerMatchingSettings _matchingSettings;
        private readonly ILogger<WorkerMatchingService> _logger;

        public WorkerMatchingService(
            IBookingRepository bookingRepository,
            IWorkerProfileRepository workerProfileRepository,
            IWorkerServiceRepository workerServiceRepository,
            IWorkerMatchingQueueRepository matchingQueueRepository,
            IWorkerLocationService workerLocationService,
            IBookingHubService bookingHubService,
            INotificationService notificationService,
            IMediaRepository mediaRepository,
            IServiceCategoryRepository serviceCategoryRepository,
            IWorkerScheduleExceptionService workerScheduleExceptionService,
            IUnitOfWork unitOfWork,
            IOptions<WorkerMatchingSettings> matchingOptions,
            ILogger<WorkerMatchingService> logger
        )
        {
            _bookingRepository =
                bookingRepository ?? throw new ArgumentNullException(nameof(bookingRepository));
            _workerProfileRepository =
                workerProfileRepository
                ?? throw new ArgumentNullException(nameof(workerProfileRepository));
            _workerServiceRepository =
                workerServiceRepository
                ?? throw new ArgumentNullException(nameof(workerServiceRepository));
            _matchingQueueRepository =
                matchingQueueRepository
                ?? throw new ArgumentNullException(nameof(matchingQueueRepository));
            _workerLocationService =
                workerLocationService
                ?? throw new ArgumentNullException(nameof(workerLocationService));
            _bookingHubService =
                bookingHubService ?? throw new ArgumentNullException(nameof(bookingHubService));
            _notificationService =
                notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _mediaRepository =
                mediaRepository ?? throw new ArgumentNullException(nameof(mediaRepository));
            _serviceCategoryRepository =
                serviceCategoryRepository ?? throw new ArgumentNullException(nameof(serviceCategoryRepository));
            _workerScheduleExceptionService =
                workerScheduleExceptionService ?? throw new ArgumentNullException(nameof(workerScheduleExceptionService));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _matchingSettings =
                matchingOptions?.Value ?? throw new ArgumentNullException(nameof(matchingOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<OperationResult> ProcessAutoMatchAsync(
            Guid bookingId,
            CancellationToken cancellationToken = default
        )
        {
            var booking = await _bookingRepository.GetByIdAsync(bookingId, cancellationToken);
            if (booking == null)
            {
                return OperationResult.Failure("Booking not found");
            }

            // 1. Find all eligible workers for this category
            var eligibleWorkers = await _workerServiceRepository.FindAsync(
                ws => ws.CategoryId == booking.CategoryId,
                cancellationToken
            );

            if (eligibleWorkers.Count == 0)
            {
                _logger.LogWarning(
                    "No workers registered for category {CategoryId}",
                    booking.CategoryId
                );
                return OperationResult.Failure("No workers available for this service category");
            }

            var eligibleWorkerProfileIds = eligibleWorkers
                .Select(ws => ws.WorkerProfileId)
                .Distinct()
                .ToList();

            // 2. Find worker profiles that are online, not busy, accepting jobs, approved, and deposit paid
            var activeWorkers = await _workerProfileRepository.FindAsync(
                wp =>
                    eligibleWorkerProfileIds.Contains(wp.Id)
                    && wp.Status == WorkerStatus.Approved, // 21/11/2026: bỏ cọc kích hoạt
                cancellationToken
            );

            if (activeWorkers.Count == 0)
            {
                _logger.LogWarning(
                    "No active workers available for booking {BookingId}",
                    bookingId
                );
                return OperationResult.Failure("No workers are currently available");
            }

            // 3. Filter by worker schedule availability
            var scheduledAtUtc = booking.ScheduledAt ?? DateTime.UtcNow;
            if (scheduledAtUtc.Kind == DateTimeKind.Unspecified)
            {
                scheduledAtUtc = DateTime.SpecifyKind(scheduledAtUtc, DateTimeKind.Utc);
            }

            var availableWorkers = new List<WorkerProfile>();
            foreach (var worker in activeWorkers)
            {
                var availabilityResult = await _workerScheduleExceptionService.IsWorkerAvailableAsync(
                    worker.Id,
                    scheduledAtUtc,
                    cancellationToken
                );

                if (availabilityResult.IsSuccess && availabilityResult.Data)
                {
                    availableWorkers.Add(worker);
                }
                else
                {
                    _logger.LogDebug(
                        "Worker {WorkerProfileId} excluded from auto-match for Booking {BookingId}: outside working schedule/exception at {ScheduledAt}. Reason: {Reason}",
                        worker.Id,
                        bookingId,
                        scheduledAtUtc,
                        availabilityResult.Message ?? "Not working/day off"
                    );
                }
            }

            if (availableWorkers.Count == 0)
            {
                _logger.LogWarning(
                    "No workers available within working schedule for booking {BookingId} at {ScheduledAt}",
                    bookingId,
                    scheduledAtUtc
                );
                return OperationResult.Failure("No workers are available at the requested time");
            }

            // 4. Calculate distance for each worker and filter by MaxDistanceKm
            var candidates = new List<(WorkerProfile Worker, double DistanceKm)>();

            foreach (var worker in availableWorkers)
            {
                // Try to get real-time location from Redis first
                double? workerLat = null;
                double? workerLng = null;

                var lastLocation = await _workerLocationService.GetLastLocationAsync(
                    worker.Id,
                    cancellationToken
                );
                if (lastLocation != null)
                {
                    workerLat = lastLocation.Lat;
                    workerLng = lastLocation.Lng;
                }
                else
                {
                    // Fall back to DB-stored location
                    workerLat = worker.CurrentLat;
                    workerLng = worker.CurrentLng;
                }

                if (!workerLat.HasValue || !workerLng.HasValue)
                {
                    continue; // Skip workers without any known location
                }

                var distanceKm = CalculateHaversineDistance(
                    booking.Lat,
                    booking.Lng,
                    workerLat.Value,
                    workerLng.Value
                );

                // Distance filter is intentionally disabled for now.
                candidates.Add((worker, distanceKm));
            }

            if (candidates.Count == 0)
            {
                _logger.LogWarning("No workers within range for booking {BookingId}", bookingId);
                return OperationResult.Failure("No workers available within service range");
            }

            // 5. Sort by RatingAvg DESC, then by DistanceKm ASC (tie-breaker)
            var sortedCandidates = candidates
                .OrderByDescending(c => c.Worker.RatingAvg)
                .ThenBy(c => c.DistanceKm)
                .ToList();

            // 6. Populate the WorkerMatchingQueues table
            var queueEntries = new List<WorkerMatchingQueue>();
            foreach (var (worker, distanceKm) in sortedCandidates)
            {
                queueEntries.Add(
                    new WorkerMatchingQueue
                    {
                        BookingId = bookingId,
                        WorkerProfileId = worker.Id,
                        AttemptNo = 1,
                        Status = MatchingStatus.Pending,
                        DistanceKm = Math.Round(distanceKm, 2),
                        Score = worker.RatingAvg,
                    }
                );
            }

            await _matchingQueueRepository.AddRangeAsync(queueEntries, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Auto-match populated {Count} candidates for booking {BookingId}",
                queueEntries.Count,
                bookingId
            );

            // 7. Offer to the first (best-ranked) worker
            return await OfferToNextWorkerAsync(bookingId, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<OperationResult> OfferToNextWorkerAsync(
            Guid bookingId,
            CancellationToken cancellationToken = default
        )
        {
            var booking = await _bookingRepository.GetByIdAsync(bookingId, cancellationToken);
            if (booking == null)
            {
                return OperationResult.Failure("Booking not found");
            }

            var nextCandidate = await _matchingQueueRepository.GetNextCandidateAsync(
                bookingId,
                cancellationToken
            );

            if (nextCandidate != null)
            {
                // Update queue entry → Offered
                nextCandidate.Status = MatchingStatus.Offered;
                nextCandidate.OfferedAt = DateTime.UtcNow;
                nextCandidate.ExpiresAt = DateTime.UtcNow.AddMinutes(
                    _matchingSettings.OfferTimeoutMinutes
                );
                _matchingQueueRepository.Update(nextCandidate);

                // Update booking → assign this worker, set status to Pending
                booking.WorkerProfileId = nextCandidate.WorkerProfileId;
                booking.Status = BookingStatus.Pending;
                booking.UpdatedDate = DateTime.UtcNow;
                _bookingRepository.Update(booking);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Booking {BookingId}: Offered to worker {WorkerId} (Rating: {Rating}, Distance: {Distance}km)",
                    bookingId,
                    nextCandidate.WorkerProfileId,
                    nextCandidate.Score,
                    nextCandidate.DistanceKm
                );

                // Notify via SignalR
                await _bookingHubService.SendStatusUpdateAsync(
                    bookingId,
                    new BookingStatusUpdateDto
                    {
                        BookingId = bookingId,
                        Status = BookingStatus.Pending.ToString(),
                        UpdatedAt = DateTime.UtcNow,
                        Message = "A worker has been found and notified. Waiting for response.",
                    },
                    cancellationToken
                );

                // Send notification to worker
                await SendNewBookingNotificationToWorkerAsync(booking, nextCandidate.WorkerProfileId, "AutoMatch", cancellationToken);

                return OperationResult.Success("Worker found and notified");
            }
            else
            {
                // No more candidates — cancel the booking instead of setting to Matching
                booking.Status = BookingStatus.Cancelled;
                booking.CancelReason = "Không có kỹ thuật viên nào nhận đơn đặt lịch.";
                booking.CancelledAt = DateTime.UtcNow;
                booking.UpdatedDate = DateTime.UtcNow;
                _bookingRepository.Update(booking);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                _logger.LogWarning(
                    "Booking {BookingId}: No more candidates available. Booking cancelled.",
                    bookingId
                );

                await _bookingHubService.SendStatusUpdateAsync(
                    bookingId,
                    new BookingStatusUpdateDto
                    {
                        BookingId = bookingId,
                        Status = BookingStatus.Cancelled.ToString(),
                        UpdatedAt = DateTime.UtcNow,
                        Message =
                            "Không có kỹ thuật viên nào nhận đơn. Đơn đặt lịch đã được hủy.",
                    },
                    cancellationToken
                );

                return OperationResult.Failure("No workers available. Booking cancelled.");
            }
        }

        /// <inheritdoc />
        public async Task<OperationResult> ProcessDirectAssignAsync(
            Guid bookingId,
            Guid workerProfileId,
            CancellationToken cancellationToken = default
        )
        {
            var booking = await _bookingRepository.GetByIdAsync(bookingId, cancellationToken);
            if (booking == null)
            {
                return OperationResult.Failure("Booking not found");
            }

            var workerProfile = await _workerProfileRepository.GetByIdAsync(workerProfileId, cancellationToken);
            if (workerProfile == null)
            {
                return OperationResult.Failure("Worker profile not found");
            }

            // 1. Create a matching queue entry for this specific worker offered directly
            var queueEntry = new WorkerMatchingQueue
            {
                BookingId = bookingId,
                WorkerProfileId = workerProfileId,
                AttemptNo = 1,
                Status = MatchingStatus.Offered,
                OfferedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_matchingSettings.OfferTimeoutMinutes),
                DistanceKm = 0,
                Score = workerProfile.RatingAvg
            };

            await _matchingQueueRepository.AddAsync(queueEntry, cancellationToken);

            // 2. Set the booking's worker and status to Pending
            booking.WorkerProfileId = workerProfileId;
            booking.Status = BookingStatus.Pending;
            booking.UpdatedDate = DateTime.UtcNow;

            _bookingRepository.Update(booking);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Booking {BookingId}: Direct assignment to worker {WorkerId} (Rating: {Rating})",
                bookingId,
                workerProfileId,
                workerProfile.RatingAvg
            );

            // 3. Notify via SignalR
            await _bookingHubService.SendStatusUpdateAsync(
                bookingId,
                new BookingStatusUpdateDto
                {
                    BookingId = bookingId,
                    Status = BookingStatus.Pending.ToString(),
                    UpdatedAt = DateTime.UtcNow,
                    Message = "Worker has been manually selected. Waiting for response.",
                },
                cancellationToken
            );

            // 4. Send notification to the selected worker
            await SendNewBookingNotificationToWorkerAsync(booking, workerProfileId, "DirectAssign", cancellationToken);

            return OperationResult.Success("Worker assigned and notified");
        }

        /// <summary>
        /// Calculates the great-circle distance between two GPS coordinates using the Haversine formula.
        /// </summary>
        /// <returns>Distance in kilometers.</returns>
        private static double CalculateHaversineDistance(
            double lat1,
            double lng1,
            double lat2,
            double lng2
        )
        {
            const double EarthRadiusKm = 6371.0;

            var dLat = DegreesToRadians(lat2 - lat1);
            var dLng = DegreesToRadians(lng2 - lng1);

            var a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(DegreesToRadians(lat1))
                    * Math.Cos(DegreesToRadians(lat2))
                    * Math.Sin(dLng / 2)
                    * Math.Sin(dLng / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return EarthRadiusKm * c;
        }

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

        /// <summary>
        /// Sends a "new booking offer" notification to the assigned worker with full booking details.
        /// </summary>
        private async Task SendNewBookingNotificationToWorkerAsync(
            Domain.Entity.Booking booking,
            Guid workerProfileId,
            string assignmentType,
            CancellationToken cancellationToken)
        {
            try
            {
                // Load worker profile to get UserId
                var workerProfile = await _workerProfileRepository.GetByIdAsync(workerProfileId, cancellationToken);
                if (workerProfile == null) return;

                // Load category name
                var category = await _serviceCategoryRepository.GetByIdAsync(booking.CategoryId, cancellationToken);
                var categoryName = category?.Name ?? "Dịch vụ";

                // Load attached images
                var images = await _mediaRepository.FindAsync(
                    m => m.OwnerId == booking.Id && m.OwnerType == MediaOwnerType.Booking && !m.IsDeleted,
                    cancellationToken);

                var meta = new
                {
                    bookingId = booking.Id,
                    assignmentType,
                    serviceName = categoryName,
                    address = booking.Address,
                    scheduledAt = booking.ScheduledAt,
                    images = images.Select(m => m.FileUrl).ToList()
                };

                await _notificationService.SendNotificationAsync(
                    workerProfile.UserId,
                    NotificationType.Booking,
                    "Bạn có yêu cầu dịch vụ mới!",
                    $"Dịch vụ {categoryName} tại {booking.Address}.",
                    $"/worker/bookings/{booking.Id}",
                    meta,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send new booking notification to worker {WorkerProfileId}", workerProfileId);
            }
        }
    }
}
