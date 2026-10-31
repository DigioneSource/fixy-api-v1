using Application.Common;
using Application.DTOs.Wallet;
using Domain.Entity;
using Domain.Enum;

namespace Application.Interfaces.Services
{
    public interface IWalletService
    {
        Task<OperationResult<WalletOverviewDto>> GetWalletOverviewAsync(
            Guid userId,
            WalletOwnerType type,
            CancellationToken cancellationToken
        );

        Task<OperationResult<PagedResponse<WalletTransactionDto>>> GetWalletTransactionsAsync(
            Guid userId,
            WalletOwnerType type,
            PagedQuery query,
            CancellationToken cancellationToken
        );
        Task<OperationResult> TopUpAsync(
            Guid userId,
            long amount,
            string externalId,
            CancellationToken cancellationToken,
            WalletOwnerType ownerType = WalletOwnerType.Customer
        );

        Task<OperationResult<WalletTransactionDto>> PayBookingAsync(
            Guid userId,
            Guid bookingId,
            CancellationToken cancellationToken
        );
        Task<OperationResult<WalletTransactionDto>> RefundAsync(
            Guid walletId,
            long amount,
            string referenceId,
            CancellationToken cancellationToken
        );

        Task<OperationResult<WalletTransactionDto>> AddWorkerIncomeAsync(
            Guid workerId,
            Guid paymentOrderId,
            long amount,
            CancellationToken cancellationToken
        );

        Task<OperationResult<WalletTransactionDto>> WithdrawAsync(
            Guid walletId,
            long amount,
            string referenceId,
            CancellationToken cancellationToken
        );

        // =============================
        // Security Deposit (Tiền cọc ký quỹ)
        // =============================

        Task<OperationResult> DepositLockAsync(
            Guid userId,
            long amount,
            string referenceId,
            CancellationToken cancellationToken
        );

        Task<OperationResult> DepositRefundAsync(
            Guid userId,
            long amount,
            string referenceId,
            CancellationToken cancellationToken
        );
    }
}
