using Application.Common;
using Application.DTOs.Payment;
using Domain.Entity;
using Domain.Enum;

namespace Application.Interfaces.Services.Payment
{
    public interface IPaymentService
    {
        Task<OperationResult<string>> CreateTopUpPaymentUrlAsync(
            Guid userId,
            long amount,
            PaymentMethod method,
            CancellationToken cancellationToken
        );
        Task<OperationResult<string>> CreateBookingPaymentUrlAsync(
            Guid bookingId,
            Guid userId,
            PaymentMethod method,
            CancellationToken cancellationToken
        );
        Task<OperationResult<bool>> HandleCallbackAsync(
            PaymentMethod method,
            Dictionary<string, string> response,
            CancellationToken cancellationToken
        );
        Task<OperationResult<bool>> HandlePayOSCallbackAsync(
            PayOSCallbackDto callback,
            CancellationToken cancellationToken
        );

        Task<OperationResult<bool>> HandleSePayIncomingAsync(
            SePayWebhookDto webhook,
            string? authorizationHeader,
            CancellationToken cancellationToken
        );

        Task<OperationResult<string>> CreateWorkerDepositPaymentUrlAsync(
            Guid workerUserId,
            PaymentMethod method,
            CancellationToken cancellationToken
        );
    }
}
