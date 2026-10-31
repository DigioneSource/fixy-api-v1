using System.Text.Json;
using Application.Common;
using Application.Common.Interfaces;
using Application.DTOs.BookingDraft;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services.Booking;
using Application.Settings;
using Domain.Entity;
using Domain.Enum;
using Infrastructure.Repositories;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BookingEntity = Domain.Entity.Booking;

namespace Infrastructure.Services
{
    public class BookingDraftService : IBookingDraftService
    {
        private readonly IDistributedCache _cache;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<BookingDraftService> _logger;
        private readonly BookingDraftSettings _draftSettings;

        private readonly ICustomerProfileRepository _customerProfileRepository;
        private readonly IBookingRepository _bookingRepository;
        private readonly IAddressRepository _addressRepository;
        private readonly IServiceCategoryRepository _serviceCategoryRepository;
        private readonly IMediaRepository _mediaRepository;
        private readonly IWorkerProfileRepository _workerProfileRepository;
        private readonly IWorkerServiceRepository _workerServiceRepository;
        private readonly IWorkerMatchingService _workerMatchingService;
        private readonly IWorkerScheduleExceptionService _workerScheduleExceptionService;
        private readonly IUnitOfWork _unitOfWork;

        public BookingDraftService(
            IDistributedCache cache,
            IDateTimeProvider dateTimeProvider,
            ICurrentUserService currentUserService,
            ILogger<BookingDraftService> logger,
            IOptions<BookingDraftSettings> draftOptions,
            ICustomerProfileRepository customerProfileRepository,
            IBookingRepository bookingRepository,
            IAddressRepository addressRepository,
            IServiceCategoryRepository serviceCategoryRepository,
            IMediaRepository mediaRepository,
            IWorkerProfileRepository workerProfileRepository,
            IWorkerServiceRepository workerServiceRepository,
            IWorkerMatchingService workerMatchingService,
            IWorkerScheduleExceptionService workerScheduleExceptionService,
            IUnitOfWork unitOfWork
        )
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _dateTimeProvider =
                dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
            _currentUserService =
                currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _draftSettings =
                draftOptions?.Value ?? throw new ArgumentNullException(nameof(draftOptions));
            _bookingRepository =
                bookingRepository ?? throw new ArgumentNullException(nameof(bookingRepository));
            _addressRepository =
                addressRepository ?? throw new ArgumentNullException(nameof(addressRepository));
            _serviceCategoryRepository =
                serviceCategoryRepository
                ?? throw new ArgumentNullException(nameof(serviceCategoryRepository));
            _mediaRepository =
                mediaRepository ?? throw new ArgumentNullException(nameof(mediaRepository));
            _workerProfileRepository =
                workerProfileRepository
                ?? throw new ArgumentNullException(nameof(workerProfileRepository));
            _workerServiceRepository =
                workerServiceRepository
                ?? throw new ArgumentNullException(nameof(workerServiceRepository));
            _workerMatchingService =
                workerMatchingService
                ?? throw new ArgumentNullException(nameof(workerMatchingService));
            _workerScheduleExceptionService =
                workerScheduleExceptionService
                ?? throw new ArgumentNullException(nameof(workerScheduleExceptionService));
            _customerProfileRepository =
                customerProfileRepository
                ?? throw new ArgumentNullException(nameof(customerProfileRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<OperationResult<BookingDraftCreatedDto>> CreateAsync(
            CreateBookingDraftRequest request,
            CancellationToken cancellationToken = default
        )
        {
            if (
                string.IsNullOrWhiteSpace(_currentUserService.UserId)
                || !Guid.TryParse(_currentUserService.UserId, out var userId)
            )
            {
                return OperationResult<BookingDraftCreatedDto>.Failure(
                    "User ID not found in token"
                );
            }

            var now = _dateTimeProvider.UtcNow;

            var ttlHours = _draftSettings.TtlHours > 0 ? _draftSettings.TtlHours : 24;

            var expiresAt = now.AddHours(ttlHours);

            var draft = new BookingDraftDto
            {
                DraftId = Guid.NewGuid(),
                UserId = userId,

                CategoryId = request.CategoryId,

                AddressId = request.AddressId,
                Address = request.Address,
                Lat = request.Lat,
                Lng = request.Lng,

                ScheduledType = request.ScheduledType,
                ScheduledAt = request.ScheduledAt,
                TotalDurationMinutes = request.TotalDurationMinutes,

                // IMPORTANT:
                // This MUST be WorkerProfileId
                WorkerProfileId = request.WorkerProfileId,

                AutoMatch = request.AutoMatch,

                CreatedAt = now,
                ExpiresAt = expiresAt,
            };

            var cacheKey = BuildDraftKey(userId, draft.DraftId);

            var payload = JsonSerializer.Serialize(draft);

            await _cache.SetStringAsync(
                cacheKey,
                payload,
                new DistributedCacheEntryOptions { AbsoluteExpiration = expiresAt },
                cancellationToken
            );

            await AddToDraftIndexAsync(userId, draft.DraftId, expiresAt, cancellationToken);

            _logger.LogInformation(
                "Booking draft created. UserId: {UserId}, DraftId: {DraftId}",
                userId,
                draft.DraftId
            );

            return OperationResult<BookingDraftCreatedDto>.Success(
                new BookingDraftCreatedDto { DraftId = draft.DraftId, ExpiresAt = expiresAt },
                "Booking draft created successfully"
            );
        }

        public async Task<OperationResult<List<BookingDraftDto>>> GetAllAsync(
            CancellationToken cancellationToken = default
        )
        {
            if (
                string.IsNullOrWhiteSpace(_currentUserService.UserId)
                || !Guid.TryParse(_currentUserService.UserId, out var userId)
            )
            {
                return OperationResult<List<BookingDraftDto>>.Failure("User ID not found in token");
            }

            var draftIds = await GetDraftIndexAsync(userId, cancellationToken);

            if (draftIds.Count == 0)
            {
                return OperationResult<List<BookingDraftDto>>.Success(new List<BookingDraftDto>());
            }

            var drafts = new List<BookingDraftDto>();
            var staleIds = new List<Guid>();

            foreach (var draftId in draftIds)
            {
                var draft = await GetDraftAsync(userId, draftId, cancellationToken);

                if (draft == null)
                {
                    staleIds.Add(draftId);
                    continue;
                }

                drafts.Add(draft);
            }

            if (staleIds.Count > 0)
            {
                await RemoveFromDraftIndexAsync(userId, staleIds, cancellationToken);
            }

            return OperationResult<List<BookingDraftDto>>.Success(
                drafts.OrderByDescending(x => x.CreatedAt).ToList(),
                "Booking drafts retrieved successfully"
            );
        }

        public async Task<OperationResult<BookingDraftDto>> GetByIdAsync(
            Guid draftId,
            CancellationToken cancellationToken = default
        )
        {
            if (
                string.IsNullOrWhiteSpace(_currentUserService.UserId)
                || !Guid.TryParse(_currentUserService.UserId, out var userId)
            )
            {
                return OperationResult<BookingDraftDto>.Failure("User ID not found in token");
            }

            var draft = await GetDraftAsync(userId, draftId, cancellationToken);

            if (draft == null)
            {
                return OperationResult<BookingDraftDto>.Failure("Booking draft not found");
            }

            return OperationResult<BookingDraftDto>.Success(
                draft,
                "Booking draft retrieved successfully"
            );
        }

        public async Task<OperationResult> UpdateAsync(
            Guid draftId,
            UpdateBookingDraftRequest request,
            CancellationToken cancellationToken = default
        )
        {
            if (
                string.IsNullOrWhiteSpace(_currentUserService.UserId)
                || !Guid.TryParse(_currentUserService.UserId, out var userId)
            )
            {
                return OperationResult.Failure("User ID not found in token");
            }

            var existingDraft = await GetDraftAsync(userId, draftId, cancellationToken);

            if (existingDraft == null)
            {
                return OperationResult.Failure("Booking draft not found");
            }

            var ttlHours = _draftSettings.TtlHours > 0 ? _draftSettings.TtlHours : 24;

            var expiresAt = _dateTimeProvider.UtcNow.AddHours(ttlHours);

            existingDraft.CategoryId = request.CategoryId;

            existingDraft.AddressId = request.AddressId;
            existingDraft.Address = request.Address;
            existingDraft.Lat = request.Lat;
            existingDraft.Lng = request.Lng;

            existingDraft.ScheduledType = request.ScheduledType;
            existingDraft.ScheduledAt = request.ScheduledAt;
            existingDraft.TotalDurationMinutes = request.TotalDurationMinutes;

            existingDraft.WorkerProfileId = request.WorkerProfileId;

            existingDraft.AutoMatch = request.AutoMatch;

            existingDraft.ExpiresAt = expiresAt;

            var cacheKey = BuildDraftKey(userId, draftId);

            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(existingDraft),
                new DistributedCacheEntryOptions { AbsoluteExpiration = expiresAt },
                cancellationToken
            );

            return OperationResult.Success("Booking draft updated successfully");
        }

        public async Task<OperationResult> DeleteAsync(
            Guid draftId,
            CancellationToken cancellationToken = default
        )
        {
            if (
                string.IsNullOrWhiteSpace(_currentUserService.UserId)
                || !Guid.TryParse(_currentUserService.UserId, out var userId)
            )
            {
                return OperationResult.Failure("User ID not found in token");
            }

            await RemoveDraftAsync(userId, draftId, cancellationToken);

            return OperationResult.Success("Booking draft deleted successfully");
        }

        public async Task<OperationResult<BookingDraftConfirmedDto>> ConfirmAsync(
            Guid draftId,
            ConfirmBookingDraftRequest request,
            CancellationToken cancellationToken = default
        )
        {
            if (
                string.IsNullOrWhiteSpace(_currentUserService.UserId)
                || !Guid.TryParse(_currentUserService.UserId, out var userId)
            )
            {
                return OperationResult<BookingDraftConfirmedDto>.Failure(
                    "User ID not found in token"
                );
            }

            // =========================
            // E-CONTRACT TERMS VALIDATION
            // =========================

            // Thiếu field hoặc khác true đều bị từ chối
            if (request == null || !request.AcceptedTerms)
            {
                return OperationResult<BookingDraftConfirmedDto>.Failure(
                    "Vui lòng đồng ý điều khoản trước khi đặt lịch"
                );
            }

            var draft = await GetDraftAsync(userId, draftId, cancellationToken);

            if (draft == null)
            {
                return OperationResult<BookingDraftConfirmedDto>.Failure("Booking draft not found");
            }

            // =========================
            // CUSTOMER PROFILE
            // =========================

            var customerProfile = await _customerProfileRepository.FirstOrDefaultAsync(
                x => x.UserId == userId,
                cancellationToken
            );

            if (customerProfile == null)
            {
                return OperationResult<BookingDraftConfirmedDto>.Failure(
                    "Customer profile not found"
                );
            }

            // =========================
            // CATEGORY
            // =========================

            var categoryExists = await _serviceCategoryRepository.ExistsByIdAsync(
                draft.CategoryId,
                cancellationToken
            );

            if (!categoryExists)
            {
                return OperationResult<BookingDraftConfirmedDto>.Failure(
                    "Service category not found"
                );
            }

            // =========================
            // ADDRESS
            // =========================

            var (addressText, lat, lng) = await ResolveAddressAsync(
                customerProfile.Id,
                draft,
                cancellationToken
            );

            if (string.IsNullOrWhiteSpace(addressText) || !lat.HasValue || !lng.HasValue)
            {
                return OperationResult<BookingDraftConfirmedDto>.Failure(
                    "Address information is invalid"
                );
            }

            // =========================
            // WORKER PROFILE
            // =========================

            Guid? workerProfileId = null;

            if (draft.WorkerProfileId.HasValue)
            {
                var workerProfile = await _workerProfileRepository.GetByIdAsync(
                    draft.WorkerProfileId.Value,
                    cancellationToken
                );

                if (workerProfile == null)
                {
                    return OperationResult<BookingDraftConfirmedDto>.Failure(
                        "Kỹ thuật viên không tồn tại"
                    );
                }

                if (workerProfile.Status != WorkerStatus.Approved)
                {
                    return OperationResult<BookingDraftConfirmedDto>.Failure(
                        "Hồ sơ Kỹ thuật viên chưa được phê duyệt hoạt động."
                    );
                }

                if (!workerProfile.IsDepositPaid)
                {
                    return OperationResult<BookingDraftConfirmedDto>.Failure(
                        "Kỹ thuật viên chưa hoàn tất ký quỹ dịch vụ, tạm thời không thể nhận lịch hẹn mới."
                    );
                }

                if (!workerProfile.IsAcceptingJobs)
                {
                    return OperationResult<BookingDraftConfirmedDto>.Failure(
                        "Kỹ thuật viên hiện đang tắt trạng thái nhận việc."
                    );
                }

                var scheduledTime = draft.ScheduledAt ?? DateTime.UtcNow;
                var availabilityResult = await _workerScheduleExceptionService.IsWorkerAvailableAsync(
                    workerProfile.Id,
                    scheduledTime,
                    cancellationToken
                );

                if (!availabilityResult.IsSuccess || !availabilityResult.Data)
                {
                    return OperationResult<BookingDraftConfirmedDto>.Failure(
                        availabilityResult.Message ?? "Kỹ thuật viên không khả dụng vào thời gian này"
                    );
                }

                workerProfileId = workerProfile.Id;
            }
            else
            {
                return OperationResult<BookingDraftConfirmedDto>.Failure(
                    "Vui lòng chọn Kỹ thuật viên để thực hiện dịch vụ"
                );
            }

            // =========================
            // ESTIMATED PRICE
            // =========================

            long? estimatedPrice = null;

            if (workerProfileId.HasValue)
            {
                var workerService = await _workerServiceRepository.GetByWorkerAndCategoryWithOptionsAsync(
                    workerProfileId.Value,
                    draft.CategoryId,
                    cancellationToken
                );

                if (workerService != null)
                {
                    if (draft.TotalDurationMinutes.HasValue && workerService.Options != null)
                    {
                        var matchingOption = workerService.Options
                            .FirstOrDefault(x => x.IsActive && x.DurationMinutes == draft.TotalDurationMinutes.Value);
                        if (matchingOption != null)
                        {
                            estimatedPrice = matchingOption.Price;
                        }
                    }

                    if (!estimatedPrice.HasValue)
                    {
                        estimatedPrice = workerService.BasePrice;
                    }
                }
            }

            if (!estimatedPrice.HasValue)
            {
                var (minPrice, _) = await _workerServiceRepository.GetPriceRangeAsync(
                    draft.CategoryId,
                    cancellationToken
                );

                estimatedPrice = minPrice;
            }

            // =========================
            // DURATION RESOLUTION
            // =========================

            int? totalDurationMinutes = draft.TotalDurationMinutes;

            if (!totalDurationMinutes.HasValue && workerProfileId.HasValue)
            {
                var workerService = await _workerServiceRepository.GetByWorkerAndCategoryWithOptionsAsync(
                    workerProfileId.Value,
                    draft.CategoryId,
                    cancellationToken
                );
                if (workerService != null)
                {
                    if (workerService.Options != null && workerService.Options.Any())
                    {
                        totalDurationMinutes = workerService.Options
                            .Where(x => x.IsActive)
                            .OrderBy(x => x.SortOrder)
                            .ThenBy(x => x.DurationMinutes)
                            .FirstOrDefault()?.DurationMinutes;
                    }

                    if (!totalDurationMinutes.HasValue && workerService.DurationMinutes > 0)
                    {
                        totalDurationMinutes = workerService.DurationMinutes;
                    }
                }
            }

            // =========================
            // CREATE BOOKING
            // =========================

            var booking = new BookingEntity
            {
                CustomerProfileId = customerProfile.Id,
                WorkerProfileId = workerProfileId,

                CategoryId = draft.CategoryId,

                Address = addressText,
                Lat = lat.Value,
                Lng = lng.Value,

                ScheduledType = draft.ScheduledType,
                ScheduledAt = draft.ScheduledAt,
                TotalDurationMinutes = totalDurationMinutes,

                Status = BookingStatus.PendingPayment,
                EstimatedPrice = estimatedPrice,
                FinalPrice = estimatedPrice,

                // E-Contract Terms Audit Trail
                AcceptedTerms = true,
                TermsAcceptedAt = _dateTimeProvider.UtcNow,
            };

            await _bookingRepository.AddAsync(booking, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await RemoveDraftAsync(userId, draftId, cancellationToken);

            return OperationResult<BookingDraftConfirmedDto>.Success(
                new BookingDraftConfirmedDto { BookingId = booking.Id },
                "Booking draft confirmed successfully"
            );
        }

        private static string BuildDraftKey(Guid userId, Guid draftId)
        {
            return $"booking:draft:{userId}:{draftId}";
        }

        private static string BuildDraftIndexKey(Guid userId)
        {
            return $"booking:draft:index:{userId}";
        }

        private async Task AddToDraftIndexAsync(
            Guid userId,
            Guid draftId,
            DateTime expiresAt,
            CancellationToken cancellationToken
        )
        {
            var indexKey = BuildDraftIndexKey(userId);

            var payload = await _cache.GetStringAsync(indexKey, cancellationToken);

            var items = string.IsNullOrWhiteSpace(payload)
                ? new List<Guid>()
                : JsonSerializer.Deserialize<List<Guid>>(payload) ?? new List<Guid>();

            if (!items.Contains(draftId))
            {
                items.Add(draftId);
            }

            await _cache.SetStringAsync(
                indexKey,
                JsonSerializer.Serialize(items),
                new DistributedCacheEntryOptions { AbsoluteExpiration = expiresAt },
                cancellationToken
            );
        }

        private async Task<List<Guid>> GetDraftIndexAsync(
            Guid userId,
            CancellationToken cancellationToken
        )
        {
            var indexKey = BuildDraftIndexKey(userId);

            var payload = await _cache.GetStringAsync(indexKey, cancellationToken);

            if (string.IsNullOrWhiteSpace(payload))
            {
                return new List<Guid>();
            }

            return JsonSerializer.Deserialize<List<Guid>>(payload) ?? new List<Guid>();
        }

        private async Task RemoveFromDraftIndexAsync(
            Guid userId,
            List<Guid> draftIds,
            CancellationToken cancellationToken
        )
        {
            var indexKey = BuildDraftIndexKey(userId);

            var payload = await _cache.GetStringAsync(indexKey, cancellationToken);

            if (string.IsNullOrWhiteSpace(payload))
            {
                return;
            }

            var items = JsonSerializer.Deserialize<List<Guid>>(payload) ?? new List<Guid>();

            items = items.Where(x => !draftIds.Contains(x)).ToList();

            if (items.Count == 0)
            {
                await _cache.RemoveAsync(indexKey, cancellationToken);

                return;
            }

            await _cache.SetStringAsync(
                indexKey,
                JsonSerializer.Serialize(items),
                cancellationToken
            );
        }

        private async Task<BookingDraftDto?> GetDraftAsync(
            Guid userId,
            Guid draftId,
            CancellationToken cancellationToken
        )
        {
            var cacheKey = BuildDraftKey(userId, draftId);

            var payload = await _cache.GetStringAsync(cacheKey, cancellationToken);

            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            return JsonSerializer.Deserialize<BookingDraftDto>(payload);
        }

        private async Task<(string? Address, double? Lat, double? Lng)> ResolveAddressAsync(
            Guid customerProfileId,
            BookingDraftDto draft,
            CancellationToken cancellationToken
        )
        {
            if (draft.AddressId.HasValue)
            {
                var address = await _addressRepository.GetByIdAsync(
                    draft.AddressId.Value,
                    cancellationToken
                );

                if (address == null || address.CustomerProfileId != customerProfileId)
                {
                    return (null, null, null);
                }

                var addressText = string.Join(
                    ", ",
                    new[] { address.Detail, address.Ward, address.City }.Where(x =>
                        !string.IsNullOrWhiteSpace(x)
                    )
                );

                return (addressText, address.Lat, address.Lng);
            }

            return (draft.Address, draft.Lat, draft.Lng);
        }

        private async Task AttachMediaAsync(
            Guid bookingId,
            Guid userId,
            List<Guid> mediaIds,
            CancellationToken cancellationToken
        )
        {
            if (mediaIds == null || mediaIds.Count == 0)
            {
                return;
            }

            var mediaItems = await _mediaRepository.FindAsync(
                x => mediaIds.Contains(x.Id) && x.UploadedById == userId,
                cancellationToken
            );

            if (mediaItems.Count == 0)
            {
                return;
            }

            foreach (var media in mediaItems)
            {
                media.OwnerId = bookingId;
                media.OwnerType = MediaOwnerType.Booking;
                media.Category = MediaCategory.Request;
            }

            _mediaRepository.UpdateRange(mediaItems);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private async Task RemoveDraftAsync(
            Guid userId,
            Guid draftId,
            CancellationToken cancellationToken
        )
        {
            var cacheKey = BuildDraftKey(userId, draftId);

            await _cache.RemoveAsync(cacheKey, cancellationToken);

            await RemoveFromDraftIndexAsync(userId, new List<Guid> { draftId }, cancellationToken);
        }
    }
}
