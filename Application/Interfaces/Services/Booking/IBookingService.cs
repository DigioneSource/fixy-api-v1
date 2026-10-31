using Application.Common;
using Application.DTOs.Booking;
using Application.DTOs.Support;

namespace Application.Interfaces.Services.Booking
{
    public interface IBookingService
    {
        Task<OperationResult<BookingDetailDto>> GetByIdAsync(Guid bookingId, CancellationToken cancellationToken = default);

        Task<OperationResult<PagedResponse<BookingDetailDto>>> GetWorkerBookingsAsync(
            WorkerBookingsQuery query,
            CancellationToken cancellationToken = default
        );

        Task<OperationResult<PagedResponse<BookingDetailDto>>> GetCustomerBookingsAsync(
            CustomerBookingsQuery query,
            CancellationToken cancellationToken = default
        );

        Task<OperationResult<PagedResponse<BookingDetailDto>>> GetAllBookingsAsync(
            AllBookingsQuery query,
            CancellationToken cancellationToken = default
        );

        Task<OperationResult<BookingAdminStatsDto>> GetAdminStatsAsync(
            AllBookingsQuery query,
            CancellationToken cancellationToken = default
        );

        // Pending --> Confirmed
        Task<OperationResult> AcceptAsync(Guid bookingId, CancellationToken cancellationToken = default);

        // Confirmed --> Traveling
        Task<OperationResult> StartTravelAsync(Guid bookingId, CancellationToken cancellationToken = default);


        // Status: Traveling -> Arrived.
        Task<OperationResult> ArriveAsync(Guid bookingId, CancellationToken cancellationToken = default);

        // Status: Arrived -> InProgress.
        Task<OperationResult> StartWorkAsync(Guid bookingId, CancellationToken cancellationToken = default);

        Task<OperationResult> CompleteAsync(Guid bookingId, CompleteBookingRequest request, CancellationToken cancellationToken = default);

        // Worker declines the booking with a reason. Updates MatchingQueue and triggers re-routing.
        Task<OperationResult> DeclineAsync(Guid bookingId, DeclineBookingRequest request, CancellationToken cancellationToken = default);

        // Worker proposes alternative price/time before accepting.
        Task<OperationResult> ProposeAsync(Guid bookingId, ProposeBookingRequest request, CancellationToken cancellationToken = default);

        // Customer accepts or rejects the worker's counter-proposal.
        Task<OperationResult> RespondProposalAsync(Guid bookingId, RespondProposalRequest request, CancellationToken cancellationToken = default);

        Task<OperationResult<SupportTicketDto>> ReportIssueAsync(Guid bookingId, ReportBookingIssueRequest request, CancellationToken cancellationToken = default);

        Task<OperationResult> ConfirmPaymentAsync(Guid bookingId, CancellationToken cancellationToken = default);

        Task<OperationResult<List<BookingMatchingQueueDto>>> GetMatchingQueueAsync(Guid bookingId, CancellationToken cancellationToken = default);

        Task<OperationResult<CancelBookingResultDto>> CancelAsync(Guid bookingId, CancelBookingRequest request, CancellationToken cancellationToken = default);

        Task<OperationResult<BookingDetailDto>> ReorderBookingAsync(Guid bookingId, CancellationToken cancellationToken = default);
    }
}
