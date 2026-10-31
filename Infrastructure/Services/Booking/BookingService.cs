using Application.Common;
using Application.Common.Interfaces;
using Application.DTOs.Booking;
using Application.DTOs.Media;
using Application.DTOs.Support;
using Application.Interfaces;
using Application.Interfaces.Hubs;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services.Booking;
using AutoMapper;
using Domain.Entity;
using Domain.Enum;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Application.Interfaces.Services;
using BookingEntity = Domain.Entity.Booking;

namespace Infrastructure.Services.Booking
{
    public class BookingService : IBookingService
    {
        private readonly IBookingRepository _bookingRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IBookingHubService _bookingHubService;
        private readonly ICurrentUserService _currentUserService;
        private readonly IMapper _mapper;
        private readonly IMediaRepository _mediaRepository;
        private readonly ISupportTicketRepository _supportTicketRepository;
        private readonly IWorkerMatchingQueueRepository _matchingQueueRepository;
        private readonly ICustomerProfileRepository _customerProfileRepository;
        private readonly IWorkerProfileRepository _workerProfileRepository;
        private readonly IWorkerMatchingService _workerMatchingService;
        private readonly INotificationService _notificationService;
        private readonly ILogger<BookingService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IBookingFinanceService _bookingFinanceService;

        private static readonly Dictionary<BookingStatus, List<BookingStatus>> AllowedTransitions = new()
        {
            { BookingStatus.PendingPayment, new List<BookingStatus> { BookingStatus.Pending, BookingStatus.Matching } },
            { BookingStatus.Pending, new List<BookingStatus> { BookingStatus.Confirmed } },
            { BookingStatus.Matching, new List<BookingStatus> { BookingStatus.Confirmed } },
            { BookingStatus.Confirmed, new List<BookingStatus> { BookingStatus.Traveling } },
            { BookingStatus.Traveling, new List<BookingStatus> { BookingStatus.Arrived } },
            { BookingStatus.Arrived, new List<BookingStatus> { BookingStatus.InProgress } },
            { BookingStatus.InProgress, new List<BookingStatus> { BookingStatus.Completed } },
        };

        public BookingService(
            IBookingRepository bookingRepository,
            IUnitOfWork unitOfWork,
            IBookingHubService bookingHubService,
            ICurrentUserService currentUserService,
            IMediaRepository mediaRepository,
            ISupportTicketRepository supportTicketRepository,
            IWorkerMatchingQueueRepository matchingQueueRepository,
            ICustomerProfileRepository customerProfileRepository,
            IWorkerProfileRepository workerProfileRepository,
            IWorkerMatchingService workerMatchingService,
            INotificationService notificationService,
            IMapper mapper,
            ILogger<BookingService> logger,
            IServiceProvider serviceProvider,
            IBookingFinanceService bookingFinanceService
        )
        {
            _bookingFinanceService =
                bookingFinanceService ?? throw new ArgumentNullException(nameof(bookingFinanceService));
            _bookingRepository =
                bookingRepository ?? throw new ArgumentNullException(nameof(bookingRepository));
            _workerProfileRepository =
                workerProfileRepository
                ?? throw new ArgumentNullException(nameof(workerProfileRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _bookingHubService =
                bookingHubService ?? throw new ArgumentNullException(nameof(bookingHubService));
            _currentUserService =
                currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
            _mediaRepository =
                mediaRepository ?? throw new ArgumentNullException(nameof(mediaRepository));
            _supportTicketRepository =
                supportTicketRepository
                ?? throw new ArgumentNullException(nameof(supportTicketRepository));
            _matchingQueueRepository =
                matchingQueueRepository
                ?? throw new ArgumentNullException(nameof(matchingQueueRepository));
            _workerMatchingService =
                workerMatchingService
                ?? throw new ArgumentNullException(nameof(workerMatchingService));
            _notificationService =
                notificationService
                ?? throw new ArgumentNullException(nameof(notificationService));
            _customerProfileRepository = customerProfileRepository;
            _workerProfileRepository = workerProfileRepository;
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        // =========================================================
        // HELPERS
        // =========================================================

        private async Task<CustomerProfile?> GetCurrentCustomerProfileAsync(
            CancellationToken cancellationToken
        )
        {
            if (!Guid.TryParse(_currentUserService.UserId, out var userId))
            {
                return null;
            }

            return await _customerProfileRepository.FirstOrDefaultAsync(
                x => x.UserId == userId,
                cancellationToken
            );
        }

        private async Task<WorkerProfile?> GetCurrentWorkerProfileAsync(
            CancellationToken cancellationToken
        )
        {
            if (!Guid.TryParse(_currentUserService.UserId, out var userId))
            {
                return null;
            }

            return await _workerProfileRepository.FirstOrDefaultAsync(
                x => x.UserId == userId,
                cancellationToken
            );
        }

        private bool IsAdmin => _currentUserService.IsInRole("ADMIN");

        /// <summary>
        /// CEO 31/10: không trả số điện thoại của khách/KTV cho bên kia — liên lạc qua ứng dụng.
        /// Chỉ ADMIN thấy số điện thoại trong đơn.
        /// </summary>
        private void ApplyContactPrivacy(BookingDetailDto dto)
        {
            if (IsAdmin)
            {
                return;
            }

            dto.WorkerPhone = null;
            dto.CustomerPhone = null;
        }

        /// <summary>Phí nền tảng / KTV thực nhận chỉ trả cho KTV của đơn và ADMIN.</summary>
        private static void HideWorkerFinance(BookingDetailDto dto)
        {
            dto.PlatformFee = null;
            dto.PlatformFeeVat = null;
            dto.WorkerNet = null;
        }

        // =========================================================
        // GET BY ID
        // =========================================================

        public async Task<OperationResult<BookingDetailDto>> GetByIdAsync(
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
                return OperationResult<BookingDetailDto>.Failure("Booking not found");
            }

            var dto = _mapper.Map<BookingDetailDto>(booking);
            ApplyContactPrivacy(dto);
            var isBookingWorker =
                booking.WorkerProfile != null
                && Guid.TryParse(_currentUserService.UserId, out var viewerId)
                && booking.WorkerProfile.UserId == viewerId;
            if (!IsAdmin && !isBookingWorker)
            {
                HideWorkerFinance(dto);
            }
            var requestImages = await _mediaRepository.GetBookingRequestImagesAsync(
                bookingId,
                cancellationToken
            );
            var completeImages = await _mediaRepository.GetBookingCompletionImagesAsync(
                bookingId,
                cancellationToken
            );
            dto.RequestImages = requestImages
                .Select(x => new MediaDto
                {
                    Id = x.Id,
                    OwnerId = x.OwnerId,
                    FileUrl = x.FileUrl,
                })
                .ToList();
            dto.CompleteImages = completeImages
                .Select(x => new MediaDto
                {
                    Id = x.Id,
                    OwnerId = x.OwnerId,
                    FileUrl = x.FileUrl,
                })
                .ToList();
            return OperationResult<BookingDetailDto>.Success(dto, "Get booking successfully");
        }

        //=================================
        // ACCEPT
        // =========================================================

        public async Task<OperationResult> AcceptAsync(
            Guid bookingId,
            CancellationToken cancellationToken = default
        )
        {
            var workerProfile = await GetCurrentWorkerProfileAsync(cancellationToken);

            if (workerProfile == null)
            {
                return OperationResult.Failure("Worker profile not found");
            }

            var booking = await _bookingRepository.GetByIdAsync(bookingId, cancellationToken);

            if (booking == null)
            {
                return OperationResult.Failure("Booking not found");
            }

            BookingStatus expectedStatus = booking.Status;
            if (expectedStatus != BookingStatus.Pending && expectedStatus != BookingStatus.Matching)
            {
                return OperationResult.Failure($"Invalid status transition. Current status: {booking.Status}");
            }

            // Mô hình trung gian: khách đã chọn KTV cụ thể — KTV khác không nhận thay được.
            if (booking.WorkerProfileId.HasValue && booking.WorkerProfileId != workerProfile.Id)
            {
                return OperationResult.Failure("Đơn này không dành cho bạn.");
            }

            return await TransitionStatusAsync(
                bookingId,
                expectedStatus,
                BookingStatus.Confirmed,
                "Kỹ thuật viên đã xác nhận đơn hàng. Đặt lịch đã được chốt.",
                async (booking) =>
                {
                    booking.WorkerProfileId = workerProfile.Id;
                    if (!booking.FinalPrice.HasValue || booking.FinalPrice <= 0)
                    {
                        booking.FinalPrice = booking.EstimatedPrice;
                    }

                    // Update matching queue entry to Accepted to prevent timeout false-positive
                    var queueEntry = await _matchingQueueRepository.GetOfferedEntryAsync(
                        bookingId,
                        workerProfile.Id,
                        cancellationToken
                    );
                    if (queueEntry != null)
                    {
                        queueEntry.Status = MatchingStatus.Accepted;
                        queueEntry.RespondedAt = DateTime.UtcNow;
                        _matchingQueueRepository.Update(queueEntry);
                    }
                },
                cancellationToken,
                beforeTransition: async (booking) =>
                {
                    // CEO 31/10: đơn tiền mặt — ví KTV phải có ≥ 40% giá đơn; đủ thì tạm giữ phí + VAT
                    // ngay trong lần lưu nhận đơn (cùng transaction với trạng thái Confirmed).
                    await _bookingRepository.LoadWorkerAndPaymentOrderAsync(booking, cancellationToken);
                    if (booking.PaymentOrder?.Method != PaymentMethod.Cash)
                    {
                        return OperationResult.Success();
                    }

                    return await _bookingFinanceService.HoldCashPlatformFeeAsync(
                        booking,
                        workerProfile.UserId,
                        cancellationToken
                    );
                }
            );
        }

        // =========================================================
        // PAYMENT CONFIRM
        // =========================================================

        public async Task<OperationResult> ConfirmPaymentAsync(
            Guid bookingId,
            CancellationToken cancellationToken = default
        )
        {
            var booking = await _bookingRepository.GetByIdAsync(bookingId, cancellationToken);
            if (booking == null)
            {
                return OperationResult.Failure("Booking not found");
            }

            BookingStatus targetStatus = booking.WorkerProfileId.HasValue
                ? BookingStatus.Pending
                : BookingStatus.Matching;

            var transitionResult = await TransitionStatusAsync(
                bookingId,
                BookingStatus.PendingPayment,
                targetStatus,
                "Thanh toán thành công. Đang chờ Kỹ thuật viên xác nhận.",
                null,
                cancellationToken
            );

            if (transitionResult.IsSuccess)
            {
                if (booking.WorkerProfileId.HasValue)
                {
                    await _workerMatchingService.ProcessDirectAssignAsync(
                        booking.Id,
                        booking.WorkerProfileId.Value,
                        cancellationToken
                    );
                }
                else
                {
                    await _workerMatchingService.ProcessAutoMatchAsync(booking.Id, cancellationToken);
                }
            }

            return transitionResult;
        }

        // =========================================================
        // START TRAVEL
        // =========================================================

        public async Task<OperationResult> StartTravelAsync(
            Guid bookingId,
            CancellationToken cancellationToken = default
        )
        {
            return await TransitionStatusAsync(
                bookingId,
                BookingStatus.Confirmed,
                BookingStatus.Traveling,
                "Worker is on the way",
                null,
                cancellationToken
            );
        }

        // =========================================================
        // ARRIVE
        // =========================================================

        public async Task<OperationResult> ArriveAsync(
            Guid bookingId,
            CancellationToken cancellationToken = default
        )
        {
            return await TransitionStatusAsync(
                bookingId,
                BookingStatus.Traveling,
                BookingStatus.Arrived,
                "Worker has arrived",
                null,
                cancellationToken
            );
        }

        // =========================================================
        // START WORK
        // =========================================================

        public async Task<OperationResult> StartWorkAsync(
            Guid bookingId,
            CancellationToken cancellationToken = default
        )
        {
            return await TransitionStatusAsync(
                bookingId,
                BookingStatus.Arrived,
                BookingStatus.InProgress,
                "Work in progress",
                null,
                cancellationToken
            );
        }

        // =========================================================
        // COMPLETE
        // =========================================================

        public async Task<OperationResult> CompleteAsync(
            Guid bookingId,
            CompleteBookingRequest request,
            CancellationToken cancellationToken = default
        )
        {
            return await TransitionStatusAsync(
                bookingId,
                BookingStatus.InProgress,
                BookingStatus.Completed,
                "Booking completed",
                async booking =>
                {
                    booking.CompletedAt = DateTime.UtcNow;

                    if (request.MediaIds != null && request.MediaIds.Any())
                    {
                        var medias = await _mediaRepository.FindAsync(
                            x => request.MediaIds.Contains(x.Id),
                            cancellationToken
                        );

                        foreach (var media in medias)
                        {
                            media.OwnerId = bookingId;
                            media.OwnerType = MediaOwnerType.Booking;
                            media.Category = MediaCategory.Completion;

                            _mediaRepository.Update(media);
                        }
                    }

                    await _bookingRepository.LoadWorkerAndPaymentOrderAsync(booking, cancellationToken);

                    if (booking.WorkerProfile != null)
                    {
                        // Increase the number of completed orders for the worker
                        booking.WorkerProfile.TotalOrders += 1;
                        _workerProfileRepository.Update(booking.WorkerProfile);
                    }

                    // Cash payments stay Pending until the service is completed.
                    // Now that the booking is done, mark Cash as Paid.
                    if (booking.PaymentOrder != null
                        && booking.PaymentOrder.Method == PaymentMethod.Cash
                        && booking.PaymentOrder.Status == PaymentStatus.Pending)
                    {
                        booking.PaymentOrder.Status = PaymentStatus.Paid;
                        booking.PaymentOrder.PaidAt = DateTime.UtcNow;
                        var paymentOrderRepo = _serviceProvider.GetRequiredService<IPaymentOrderRepository>();
                        paymentOrderRepo.Update(booking.PaymentOrder);
                    }

                    // CEO 31/10: chốt phí nền tảng 30% + VAT 10% và quyết toán ví KTV
                    // trong cùng lần lưu với trạng thái Completed (một transaction).
                    await _bookingFinanceService.SettleCompletedBookingAsync(booking, cancellationToken);
                },
                cancellationToken
            );
        }

        // =========================================================
        // DECLINE
        // =========================================================

        public async Task<OperationResult> DeclineAsync(
            Guid bookingId,
            DeclineBookingRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var workerProfile = await GetCurrentWorkerProfileAsync(cancellationToken);

            if (workerProfile == null)
            {
                return OperationResult.Failure("Worker profile not found");
            }

            var booking = await _bookingRepository.GetByIdAsync(bookingId, cancellationToken);

            if (booking == null)
            {
                return OperationResult.Failure("Booking not found");
            }

            if (booking.WorkerProfileId != workerProfile.Id)
            {
                return OperationResult.Failure("Forbidden");
            }
            var queueEntry = await _matchingQueueRepository.GetOfferedEntryAsync(
                bookingId,
                workerProfile.Id,
                cancellationToken
            );
            if (queueEntry == null)
            {
                return OperationResult.Failure("No active offer found for this worker and booking");
            }

            // Mark as rejected
            queueEntry.Status = MatchingStatus.Rejected;
            queueEntry.RejectReason = request.RejectReason;
            queueEntry.RespondedAt = DateTime.UtcNow;
            _matchingQueueRepository.Update(queueEntry);

            // Cancel the booking instead of auto-forwarding to another worker
            booking.Status = BookingStatus.Cancelled;
            booking.CancelReason = $"Kỹ thuật viên đã từ chối đơn. Lý do: {request.RejectReason ?? "Không có lý do"}";
            booking.CancelledAt = DateTime.UtcNow;
            booking.UpdatedDate = DateTime.UtcNow;
            _bookingRepository.Update(booking);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Worker {WorkerId} declined booking {BookingId}. Reason: {Reason}. Booking cancelled.",
                workerProfile.Id,
                bookingId,
                request.RejectReason
            );

            // Notify customer via SignalR that the booking has been cancelled
            await _bookingHubService.SendStatusUpdateAsync(
                bookingId,
                new BookingStatusUpdateDto
                {
                    BookingId = bookingId,
                    Status = BookingStatus.Cancelled.ToString(),
                    UpdatedAt = DateTime.UtcNow,
                    Message = "Kỹ thuật viên đã từ chối đơn đặt lịch của bạn.",
                },
                cancellationToken
            );

            return OperationResult.Success("Booking declined and cancelled successfully");
        }

        // =========================================================
        // PROPOSE
        // =========================================================

        public async Task<OperationResult> ProposeAsync(
            Guid bookingId,
            ProposeBookingRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var workerProfile = await GetCurrentWorkerProfileAsync(cancellationToken);

            if (workerProfile == null)
            {
                return OperationResult.Failure("Worker profile not found");
            }

            var booking = await _bookingRepository.GetByIdAsync(bookingId, cancellationToken);

            if (booking == null)
            {
                return OperationResult.Failure("Booking not found");
            }

            if (booking.WorkerProfileId != workerProfile.Id)
            {
                return OperationResult.Failure("Forbidden");
            }

            if (!request.ProposedPrice.HasValue || request.ProposedPrice.Value <= 0)
            {
                return OperationResult.Failure("Giá đề xuất phải lớn hơn 0.");
            }

            booking.UpdatedDate = DateTime.UtcNow;

            _bookingRepository.Update(booking);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return OperationResult.Success("Proposal submitted successfully");
        }

        // =========================================================
        // RESPOND PROPOSAL
        // =========================================================

        public async Task<OperationResult> RespondProposalAsync(
            Guid bookingId,
            RespondProposalRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var customerProfile = await GetCurrentCustomerProfileAsync(cancellationToken);

            if (customerProfile == null)
            {
                return OperationResult.Failure("Customer profile not found");
            }

            var booking = await _bookingRepository.GetByIdAsync(bookingId, cancellationToken);

            if (booking == null)
            {
                return OperationResult.Failure("Booking not found");
            }

            // IMPORTANT
            if (booking.CustomerProfileId != customerProfile.Id)
            {
                return OperationResult.Failure("Forbidden");
            }

            if (request.Accept)
            {
                booking.FinalPrice = booking.EstimatedPrice;
                booking.Status = BookingStatus.PendingPayment;
            }
            else
            {
                booking.WorkerProfileId = null;

                booking.Status = BookingStatus.Matching;
            }

            booking.UpdatedDate = DateTime.UtcNow;

            _bookingRepository.Update(booking);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return OperationResult.Success(
                request.Accept ? "Proposal accepted" : "Proposal rejected"
            );
        }

        // =========================================================
        // REPORT ISSUE
        // =========================================================

        public async Task<OperationResult<SupportTicketDto>> ReportIssueAsync(
            Guid bookingId,
            ReportBookingIssueRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var workerProfile = await GetCurrentWorkerProfileAsync(cancellationToken);

            if (workerProfile == null)
            {
                return OperationResult<SupportTicketDto>.Failure("Worker profile not found");
            }

            var booking = await _bookingRepository.GetByIdAsync(bookingId, cancellationToken);

            if (booking == null)
            {
                return OperationResult<SupportTicketDto>.Failure("Booking not found");
            }

            if (booking.WorkerProfileId != workerProfile.Id)
            {
                return OperationResult<SupportTicketDto>.Failure("Forbidden");
            }

            var ticket = new SupportTicket
            {
                BookingId = bookingId,
                ReporterId = workerProfile.Id,
                ReporterType = SupportReporterType.Worker,
                Subject = request.Subject,
                Category = request.Category,
                Priority = request.Priority,
                Status = SupportStatus.Open,
                CreatedDate = DateTime.UtcNow,
                Messages = new List<SupportMessage>
                {
                    new()
                    {
                        SenderId = workerProfile.Id,
                        Content = request.Description,
                        CreatedDate = DateTime.UtcNow,
                    },
                },
            };

            await _supportTicketRepository.AddAsync(ticket, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var dto = _mapper.Map<SupportTicketDto>(ticket);

            return OperationResult<SupportTicketDto>.Success(dto, "Issue reported successfully");
        }

        // =========================================================
        // GET WORKER BOOKINGS
        // =========================================================

        public async Task<OperationResult<PagedResponse<BookingDetailDto>>> GetWorkerBookingsAsync(
            WorkerBookingsQuery query,
            CancellationToken cancellationToken = default
        )
        {
            var workerProfile = await GetCurrentWorkerProfileAsync(cancellationToken);

            if (workerProfile == null)
            {
                return OperationResult<PagedResponse<BookingDetailDto>>.Failure(
                    "Worker profile not found"
                );
            }

            var (items, totalCount) = await _bookingRepository.GetWorkerBookingsAsync(
                workerProfile.Id,
                query,
                cancellationToken
            );

            var dtos = _mapper.Map<List<BookingDetailDto>>(items);
            dtos.ForEach(ApplyContactPrivacy);

            return OperationResult<PagedResponse<BookingDetailDto>>.Success(
                new PagedResponse<BookingDetailDto>
                {
                    Items = dtos,
                    TotalCount = totalCount,
                    PageNumber = query.PageNumber,
                    PageSize = query.PageSize,
                },
                "Worker bookings retrieved successfully"
            );
        }

        // =========================================================
        // GET CUSTOMER BOOKINGS
        // =========================================================

        public async Task<
            OperationResult<PagedResponse<BookingDetailDto>>
        > GetCustomerBookingsAsync(
            CustomerBookingsQuery query,
            CancellationToken cancellationToken = default
        )
        {
            var customerProfile = await GetCurrentCustomerProfileAsync(cancellationToken);

            if (customerProfile == null)
            {
                return OperationResult<PagedResponse<BookingDetailDto>>.Failure(
                    "Customer profile not found"
                );
            }

            var (items, totalCount) = await _bookingRepository.GetCustomerBookingsAsync(
                customerProfile.Id,
                query,
                cancellationToken
            );
            var dtos = _mapper.Map<List<BookingDetailDto>>(items);
            dtos.ForEach(ApplyContactPrivacy);
            dtos.ForEach(HideWorkerFinance);

            var response = new PagedResponse<BookingDetailDto>
            {
                Items = dtos,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize,
                TotalCount = totalCount,
            };

            return OperationResult<PagedResponse<BookingDetailDto>>.Success(
                response,
                "Customer bookings retrieved successfully"
            );
        }

        public async Task<OperationResult<PagedResponse<BookingDetailDto>>> GetAllBookingsAsync(
            AllBookingsQuery query,
            CancellationToken cancellationToken = default
        )
        {
            var (items, totalCount) = await _bookingRepository.GetAllBookingsAsync(
                query,
                cancellationToken
            );

            var dtos = _mapper.Map<List<BookingDetailDto>>(items);

            var response = new PagedResponse<BookingDetailDto>
            {
                Items = dtos,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize,
                TotalCount = totalCount,
            };

            return OperationResult<PagedResponse<BookingDetailDto>>.Success(
                response,
                "All bookings retrieved successfully"
            );
        }

        public async Task<OperationResult<BookingAdminStatsDto>> GetAdminStatsAsync(
            AllBookingsQuery query,
            CancellationToken cancellationToken = default
        )
        {
            var stats = await _bookingRepository.GetAdminStatsAsync(query, cancellationToken);
            return OperationResult<BookingAdminStatsDto>.Success(stats, "Admin booking statistics retrieved successfully");
        }

        public async Task<OperationResult<List<BookingMatchingQueueDto>>> GetMatchingQueueAsync(
            Guid bookingId,
            CancellationToken cancellationToken = default
        )
        {
            var matchingQueue = await _matchingQueueRepository.GetQueueForBookingAsync(
                bookingId,
                cancellationToken
            );

            var dtos = matchingQueue
                .Select(q => new BookingMatchingQueueDto
                {
                    WorkerId = q.WorkerProfileId,
                    FullName = q.Worker?.User?.FullName ?? string.Empty,
                    Phone = IsAdmin ? q.Worker?.User?.Phone ?? string.Empty : string.Empty,
                    AvatarUrl = q.Worker?.User?.AvatarUrl,
                    RatingAvg = q.Worker?.RatingAvg ?? 0,
                    DistanceKm = q.DistanceKm,
                    Score = q.Score,
                    Status = q.Status.ToString(),
                    OfferedAt = q.OfferedAt,
                    ExpiresAt = q.ExpiresAt,
                    RejectReason = q.RejectReason,
                })
                .ToList();

            return OperationResult<List<BookingMatchingQueueDto>>.Success(
                dtos,
                "Matching queue retrieved successfully"
            );
        }

        /// <summary>
        /// Attempts to offer the booking to the next candidate worker in the matching queue.
        /// If no candidates remain, the booking status is set back to Matching.
        /// </summary>
        private async Task OfferToNextWorkerAsync(
            BookingEntity booking,
            CancellationToken cancellationToken
        )
        {
            var nextCandidate = await _matchingQueueRepository.GetNextCandidateAsync(
                booking.Id,
                cancellationToken
            );

            if (nextCandidate != null)
            {
                nextCandidate.Status = MatchingStatus.Offered;
                nextCandidate.OfferedAt = DateTime.UtcNow;
                nextCandidate.ExpiresAt = DateTime.UtcNow.AddMinutes(15); // Default timeout, will be read from PlatformConfig in the future
                _matchingQueueRepository.Update(nextCandidate);

                _logger.LogInformation(
                    "Offered booking {BookingId} to next worker {WorkerId}",
                    booking.Id,
                    nextCandidate.WorkerProfileId
                );
            }
            else
            {
                // No more candidates � set booking back to Matching so the system can re-search
                booking.Status = BookingStatus.Matching;
                booking.UpdatedDate = DateTime.UtcNow;
                _bookingRepository.Update(booking);

                _logger.LogWarning(
                    "No more candidates for booking {BookingId}. Status set to Matching.",
                    booking.Id
                );
            }
        }

        // =========================================================
        // TRANSITION
        // =========================================================

        private async Task<OperationResult> TransitionStatusAsync(
            Guid bookingId,
            BookingStatus expectedCurrentStatus,
            BookingStatus newStatus,
            string message,
            Func<BookingEntity, Task>? onTransition,
            CancellationToken cancellationToken,
            Func<BookingEntity, Task<OperationResult>>? beforeTransition = null
        )
        {
            var booking = await _bookingRepository.GetByIdAsync(bookingId, cancellationToken);

            if (booking == null)
            {
                return OperationResult.Failure("Booking not found");
            }

            if (booking.Status != expectedCurrentStatus)
            {
                return OperationResult.Failure(
                    $"Invalid status transition. Current status: {booking.Status}"
                );
            }

            if (
                !AllowedTransitions.TryGetValue(expectedCurrentStatus, out var allowedNextList)
                || !allowedNextList.Contains(newStatus)
            )
            {
                return OperationResult.Failure("Transition is not allowed");
            }

            // Kiểm tra có thể từ chối việc đổi trạng thái (VD: ký quỹ không đủ) — chạy trước mọi thay đổi.
            if (beforeTransition != null)
            {
                var guardResult = await beforeTransition(booking);
                if (!guardResult.IsSuccess)
                {
                    return guardResult;
                }
            }

            booking.Status = newStatus;
            booking.UpdatedDate = DateTime.UtcNow;

            if (booking.WorkerProfileId.HasValue)
            {
                var workerProfile = await _workerProfileRepository.GetByIdAsync(booking.WorkerProfileId.Value, cancellationToken);
                if (workerProfile != null)
                {
                    if (newStatus == BookingStatus.Confirmed || newStatus == BookingStatus.Traveling || newStatus == BookingStatus.Arrived || newStatus == BookingStatus.InProgress)
                    {
                        workerProfile.IsBusy = true;
                        _workerProfileRepository.Update(workerProfile);
                    }
                    else if (newStatus == BookingStatus.Completed || newStatus == BookingStatus.Cancelled)
                    {
                        workerProfile.IsBusy = false;
                        _workerProfileRepository.Update(workerProfile);
                    }
                }
            }

            if (onTransition != null)
            {
                await onTransition(booking);
            }

            _bookingRepository.Update(booking);

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException ex)
            {
                // Ví KTV dùng RowVersion: hai thao tác ghi cùng lúc thì một bên phải thử lại.
                _logger.LogWarning(ex, "Concurrency conflict while transitioning booking {BookingId}", bookingId);
                return OperationResult.Failure("Dữ liệu vừa thay đổi, vui lòng thử lại.");
            }

            await _bookingHubService.SendStatusUpdateAsync(
                booking.Id,
                new BookingStatusUpdateDto
                {
                    BookingId = booking.Id,
                    Status = booking.Status.ToString(),
                    UpdatedAt = DateTime.UtcNow,
                    Message = message,
                },
                cancellationToken
            );

            // Send notification to customer on status changes
            await SendBookingStatusNotificationAsync(booking, newStatus, message, cancellationToken);

            return OperationResult.Success(message);
        }

        /// <summary>
        /// Sends a notification to the customer when the booking status changes.
        /// </summary>
        private async Task SendBookingStatusNotificationAsync(
            BookingEntity booking,
            BookingStatus newStatus,
            string message,
            CancellationToken cancellationToken)
        {
            try
            {
                // Load customer profile to get UserId
                var customerProfile = await _customerProfileRepository.GetByIdAsync(
                    booking.CustomerProfileId, cancellationToken);
                if (customerProfile == null) return;

                // Build notification title based on status
                var (title, body) = newStatus switch
                {
                    BookingStatus.PendingPayment => ("Vui lòng thanh toán đơn hàng", "Đơn dịch vụ đã được tạo. Vui lòng hoàn tất thanh toán để xác nhận đặt lịch."),
                    BookingStatus.Confirmed => ("Kỹ thuật viên đã tiếp nhận đơn", "Kỹ thuật viên đã xác nhận đơn hàng và sẽ liên hệ với bạn sớm."),
                    BookingStatus.Traveling => ("Kỹ thuật viên đang di chuyển đến", "Kỹ thuật viên đang trên đường đến địa chỉ của bạn."),
                    BookingStatus.Arrived => ("Kỹ thuật viên đã đến nơi", "Kỹ thuật viên đã có mặt tại địa chỉ của bạn."),
                    BookingStatus.InProgress => ("Đang tiến hành dịch vụ", "Kỹ thuật viên đang thực hiện dịch vụ."),
                    BookingStatus.Completed => ("Đơn hàng đã hoàn thành", "Dịch vụ đã hoàn tất. Hãy để lại đánh giá cho kỹ thuật viên nhé!"),
                    _ => ((string?)null, (string?)null)
                };

                if (title == null) return;

                var meta = new
                {
                    bookingId = booking.Id,
                    status = newStatus.ToString()
                };

                await _notificationService.SendNotificationAsync(
                    customerProfile.UserId,
                    NotificationType.Booking,
                    title,
                    body!,
                    $"/customer/bookings/{booking.Id}",
                    meta,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send booking status notification for booking {BookingId}", booking.Id);
            }
        }

        public async Task<OperationResult> CancelAsync(Guid bookingId,CancelBookingRequest request,CancellationToken cancellationToken = default)
        {
            var currentUserIdStr = _currentUserService.UserId;
            if (string.IsNullOrEmpty(currentUserIdStr) || !Guid.TryParse(currentUserIdStr, out var currentUserId))
            {
                return OperationResult.Failure("Không tìm thấy thông tin người dùng hiện tại");
            }

            var booking = await _bookingRepository.GetBookingWithWorkerAsync(bookingId, cancellationToken);
            if (booking == null)
            {
                return OperationResult.Failure("Không tìm thấy thông tin lịch hẹn");
            }

            // 1. Kiểm tra Quyền sở hữu (Authorization Check)
            var isCustomer = booking.CustomerProfile?.UserId == currentUserId;
            var isWorker = booking.WorkerProfile?.UserId == currentUserId;

            if (!isCustomer && !isWorker)
            {
                return OperationResult.Failure("Bạn không có quyền hủy lịch hẹn này");
            }

            // 2. Kiểm tra Trạng thái hợp lệ theo từng Vai trò (Role-based Status Validation)
            if (isCustomer)
            {
                var customerValidStates = new[]
                {
                    BookingStatus.Matching,
                    BookingStatus.Pending,
                    BookingStatus.PendingPayment,
                    BookingStatus.Confirmed
                };

                if (!customerValidStates.Contains(booking.Status))
                {
                    return OperationResult.Failure($"Khách hàng không thể hủy lịch hẹn ở trạng thái hiện tại: {booking.Status}");
                }
            }
            else if (isWorker)
            {
                // Worker chỉ được phép hủy lịch hẹn khi ở trạng thái Pending
                if (booking.Status != BookingStatus.Pending && booking.Status != BookingStatus.Matching)
                {
                    return OperationResult.Failure($"Kỹ thuật viên không thể hủy lịch hẹn ở trạng thái hiện tại: {booking.Status}");
                }
            }

            // 3. Khởi tạo Database Transaction
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                // Cập nhật thông tin hủy trên Booking
                booking.Status = BookingStatus.Cancelled;
                booking.CancelledById = currentUserId;
                booking.CancelledAt = DateTime.UtcNow;
                booking.CancelReason = request.Reason;
                booking.UpdatedDate = DateTime.UtcNow;

                if (booking.WorkerProfileId.HasValue)
                {
                    var workerProfile = await _workerProfileRepository.GetByIdAsync(booking.WorkerProfileId.Value, cancellationToken);
                    if (workerProfile != null)
                    {
                        workerProfile.IsBusy = false;
                        _workerProfileRepository.Update(workerProfile);
                    }
                }

                _bookingRepository.Update(booking);

                // A. Giải phóng Voucher nếu có áp dụng
                var voucherService = _serviceProvider.GetRequiredService<Application.Interfaces.Services.Voucher.IVoucherService>();
                await voucherService.ReleaseVoucherAsync(bookingId, cancellationToken);

                // B. Xử lý Hoàn tiền ví nếu đã thanh toán
                if (booking.PaymentOrder != null && booking.PaymentOrder.Status == PaymentStatus.Paid)
                {
                    var walletService = _serviceProvider.GetRequiredService<IWalletService>();

                    var refundResult = await walletService.RefundAsync(
                        booking.CustomerProfile!.UserId,
                        booking.PaymentOrder.FinalAmount,
                        bookingId.ToString(),
                        cancellationToken
                    );

                    if (!refundResult.IsSuccess)
                    {
                        await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                        return OperationResult.Failure($"Lỗi hoàn tiền về ví khách hàng: {refundResult.Message}");
                    }

                    booking.PaymentOrder.Status = PaymentStatus.Refunded;
                    booking.PaymentOrder.RefundedAt = DateTime.UtcNow;
                    var paymentOrderRepository = _serviceProvider.GetRequiredService<IPaymentOrderRepository>();
                    paymentOrderRepository.Update(booking.PaymentOrder);
                }

                // B2. Đơn tiền mặt: hoàn khoản tạm giữ phí nền tảng về ví KTV (CEO 31/10)
                if (booking.WorkerProfile != null)
                {
                    await _bookingFinanceService.ReleaseCashPlatformFeeAsync(
                        booking,
                        booking.WorkerProfile.UserId,
                        cancellationToken
                    );
                }

                // C. Làm sạch Hàng đợi Matching (Đánh dấu Skipped cho các Worker khác đang được offer đơn này)
                var queueEntries = await _matchingQueueRepository.FindAsync(
                    x => x.BookingId == bookingId && (x.Status == MatchingStatus.Pending || x.Status == MatchingStatus.Offered),
                    cancellationToken
                );
                foreach (var entry in queueEntries)
                {
                    entry.Status = MatchingStatus.Skipped;
                    _matchingQueueRepository.Update(entry);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                // 4. Đồng bộ thời gian thực qua SignalR & Gửi thông báo đẩy (Push)
                await _bookingHubService.SendStatusUpdateAsync(
                    bookingId,
                    new BookingStatusUpdateDto
                    {
                        BookingId = bookingId,
                        Status = BookingStatus.Cancelled.ToString(),
                        UpdatedAt = DateTime.UtcNow,
                        Message = $"Lịch hẹn đã bị hủy bởi {(isCustomer ? "Khách hàng" : "Thợ")}"
                    },
                    cancellationToken
                );

                // Gửi thông báo đẩy cho bên đối tác
                if (isCustomer && booking.WorkerProfile != null)
                {
                    await _notificationService.SendNotificationAsync(
                        booking.WorkerProfile.UserId,
                        NotificationType.Booking,
                        "Khách hàng đã hủy lịch hẹn",
                        $"Đơn hàng #{booking.Id.ToString()[..8]} đã bị hủy bởi khách hàng.",
                        $"/worker/bookings/{bookingId}",
                        new { bookingId = bookingId, status = BookingStatus.Cancelled.ToString() },
                        cancellationToken: cancellationToken
                    );
                }
                else if (isWorker && booking.CustomerProfile != null)
                {
                    await _notificationService.SendNotificationAsync(
                        booking.CustomerProfile.UserId,
                        NotificationType.Booking,
                        "Kỹ thuật viên đã hủy lịch hẹn",
                        $"Kỹ thuật viên đã hủy lịch hẹn #{booking.Id.ToString()[..8]}. Chúng tôi sẽ tìm kỹ thuật viên khác cho bạn.",
                        $"/customer/bookings/{bookingId}",
                        new { bookingId = bookingId, status = BookingStatus.Cancelled.ToString() },
                        cancellationToken: cancellationToken
                    );
                }

                return OperationResult.Success("Hủy lịch hẹn thành công");
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                _logger.LogError(ex, "Lỗi xảy ra khi hủy lịch hẹn {BookingId}", bookingId);
                return OperationResult.Failure("Có lỗi xảy ra trong quá trình xử lý yêu cầu hủy");
            }
        }

        public async Task<OperationResult<BookingDetailDto>> ReorderBookingAsync(
            Guid bookingId,
            CancellationToken cancellationToken = default
        )
        {
            var customerProfile = await GetCurrentCustomerProfileAsync(cancellationToken);
            if (customerProfile == null)
            {
                return OperationResult<BookingDetailDto>.Failure("Không tìm thấy thông tin khách hàng");
            }

            var originalBooking = await _bookingRepository.GetByIdAsync(bookingId, cancellationToken);
            if (originalBooking == null)
            {
                return OperationResult<BookingDetailDto>.Failure("Không tìm thấy thông tin lịch hẹn gốc");
            }

            if (originalBooking.CustomerProfileId != customerProfile.Id)
            {
                return OperationResult<BookingDetailDto>.Failure("Bạn không có quyền đặt lại lịch hẹn này");
            }

            if (!originalBooking.WorkerProfileId.HasValue)
            {
                return OperationResult<BookingDetailDto>.Failure("Lịch hẹn gốc không có thông tin thợ để thực hiện đặt lại");
            }

            var workerProfileId = originalBooking.WorkerProfileId.Value;
            var workerProfile = await _workerProfileRepository.GetByIdAsync(workerProfileId, cancellationToken);
            if (workerProfile == null || workerProfile.Status != WorkerStatus.Approved)
            {
                return OperationResult<BookingDetailDto>.Failure("Kỹ thuật viên hiện không hoạt động hoặc chưa được duyệt");
            }

            var workerServiceRepository = _serviceProvider.GetRequiredService<IWorkerServiceRepository>();
            var workerService = await workerServiceRepository.FirstOrDefaultAsync(
                x => x.WorkerProfileId == workerProfileId && x.CategoryId == originalBooking.CategoryId,
                cancellationToken
            );

            if (workerService == null || !workerService.IsActive)
            {
                return OperationResult<BookingDetailDto>.Failure("Kỹ thuật viên này hiện không còn cung cấp dịch vụ thuộc danh mục này");
            }

            if (workerService.BasePrice <= 0)
            {
                return OperationResult<BookingDetailDto>.Failure("Gói dịch vụ này chưa có giá niêm yết, không thể đặt.");
            }

            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                var newBooking = new BookingEntity
                {
                    CustomerProfileId = customerProfile.Id,
                    WorkerProfileId = workerProfileId,
                    CategoryId = originalBooking.CategoryId,
                    Address = originalBooking.Address,
                    Lat = originalBooking.Lat,
                    Lng = originalBooking.Lng,
                    ScheduledType = BookingScheduledType.Now,
                    Status = BookingStatus.Pending,
                    EstimatedPrice = workerService.BasePrice,
                    ReorderFromId = originalBooking.Id
                };

                await _bookingRepository.AddAsync(newBooking, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _workerMatchingService.ProcessDirectAssignAsync(newBooking.Id, workerProfileId, cancellationToken);

                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                var detailResult = await GetByIdAsync(newBooking.Id, cancellationToken);
                if (!detailResult.IsSuccess)
                {
                    return OperationResult<BookingDetailDto>.Failure("Không thể lấy thông tin chi tiết của lịch hẹn mới");
                }

                return OperationResult<BookingDetailDto>.Success(detailResult.Data!, "Đặt lại dịch vụ thành công");
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                _logger.LogError(ex, "Lỗi xảy ra khi đặt lại lịch hẹn {BookingId}", bookingId);
                return OperationResult<BookingDetailDto>.Failure("Có lỗi xảy ra trong quá trình đặt lại dịch vụ");
            }
        }
    }
}
