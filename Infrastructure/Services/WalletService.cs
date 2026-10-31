using Application.Common;
using Application.DTOs.Wallet;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Interfaces.Services.Booking;
using Domain.Entity;
using Domain.Enum;
using Microsoft.EntityFrameworkCore;

public class WalletService : IWalletService
{
    private readonly IUserRepository _userRepository;
    private readonly IWalletRepository _walletRepository;
    private readonly IWalletTransactionRepository _walletTransactionRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly IPaymentOrderRepository _paymentOrderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBookingService _bookingService;

    public WalletService(
        IUserRepository userRepository,
        IWalletRepository walletRepository,
        IWalletTransactionRepository walletTransactionRepository,
        IBookingRepository bookingRepository,
        IPaymentOrderRepository paymentOrderRepository,
        IUnitOfWork unitOfWork,
        IBookingService bookingService
    )
    {
        _userRepository = userRepository;
        _walletRepository = walletRepository;
        _walletTransactionRepository = walletTransactionRepository;
        _bookingRepository = bookingRepository;
        _paymentOrderRepository = paymentOrderRepository;
        _unitOfWork = unitOfWork;
        _bookingService = bookingService;
    }

    public async Task<OperationResult<WalletOverviewDto>> GetWalletOverviewAsync(
        Guid userId,
        WalletOwnerType type,
        CancellationToken cancellationToken
    )
    {
        var wallet = await _walletRepository.GetByUserIdAsync(userId, type, cancellationToken);

        if (wallet == null)
            return OperationResult<WalletOverviewDto>.Failure("Wallet not found");

        var recentTransactions = await _walletTransactionRepository.GetRecentByWalletIdAsync(
            wallet.Id,
            10,
            cancellationToken
        );

        return OperationResult<WalletOverviewDto>.Success(
            new WalletOverviewDto
            {
                Id = wallet.Id,
                Balance = wallet.Balance,
                LockedBalance = wallet.LockedBalance,
                LifetimeEarned = wallet.LifetimeEarned,
                LifetimeSpent = wallet.LifetimeSpent,
                RecentTransactions = recentTransactions
                    .Select(t => new WalletTransactionDto
                    {
                        Id = t.Id,
                        Type = t.Type.ToString(),
                        Direction = t.Direction.ToString(),
                        Amount = t.Amount,
                        BalanceAfter = t.BalanceAfter,
                        BalanceBefore = t.BalanceBefore,
                        CreatedDate = t.CreatedDate,
                        Status = t.Status.ToString(),
                    })
                    .ToList(),
            },
            "Get wallet overview successfully"
        );
    }

    public async Task<
        OperationResult<PagedResponse<WalletTransactionDto>>
    > GetWalletTransactionsAsync(
        Guid userId,
        WalletOwnerType type,
        PagedQuery query,
        CancellationToken cancellationToken
    )
    {
        var wallet = await _walletRepository.GetByUserIdAsync(userId, type, cancellationToken);

        if (wallet == null)
        {
            return OperationResult<PagedResponse<WalletTransactionDto>>.Failure("Wallet not found");
        }

        var result = await _walletTransactionRepository.GetPagedByWalletIdAsync(
            wallet.Id,
            query,
            cancellationToken
        );

        return OperationResult<PagedResponse<WalletTransactionDto>>.Success(
            new PagedResponse<WalletTransactionDto>
            {
                Items = result
                    .Item1.Select(t => new WalletTransactionDto
                    {
                        Id = t.Id,
                        Type = t.Type.ToString(),
                        Direction = t.Direction.ToString(),
                        Amount = t.Amount,
                        BalanceAfter = t.BalanceAfter,
                        BalanceBefore = t.BalanceBefore,
                        CreatedDate = t.CreatedDate,
                        Status = t.Status.ToString(),
                    })
                    .ToList(),
                TotalCount = result.Item2,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize,
            },
            "Get transactions successfully"
        );
    }

    public async Task<OperationResult> TopUpAsync(
        Guid userId,
        long amount,
        string externalId,
        CancellationToken cancellationToken,
        WalletOwnerType ownerType = WalletOwnerType.Customer
    )
    {
        if (await _walletTransactionRepository.ExistsExternalTransactionAsync(externalId))
            return OperationResult.Failure("Duplicate transaction");

        await _unitOfWork.BeginTransactionAsync();

        try
        {
        var wallet = await _walletRepository.GetByUserIdAsync(
            userId,
            ownerType,
            cancellationToken
        );

        if (wallet == null)
            return OperationResult.Failure("Wallet not found");

            var before = wallet.Balance;

            wallet.Balance += amount;
            // Tiền KTV nạp không phải thu nhập → không cộng LifetimeEarned của ví KTV.
            if (ownerType == WalletOwnerType.Customer)
            {
                wallet.LifetimeEarned += amount;
            }

            var tx = new WalletTransaction
            {
                WalletId = wallet.Id,
                Type = WalletTransactionType.TopUp,
                Direction = WalletDirection.Credit,
                Amount = amount,
                BalanceBefore = before,
                BalanceAfter = wallet.Balance,
                Status = TransactionStatus.Success,
                ExternalTransactionId = externalId,
            };

            await _walletTransactionRepository.AddAsync(tx, cancellationToken);
            _walletRepository.Update(wallet);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync();

            return OperationResult.Success("Topup success");
        }
        catch (DbUpdateConcurrencyException)
        {
            await _unitOfWork.RollbackTransactionAsync();
            return OperationResult.Failure("Wallet conflict, retry again");
        }
    }

    public async Task<OperationResult<WalletTransactionDto>> PayBookingAsync(
        Guid userId,
        Guid bookingId,
        CancellationToken cancellationToken
    )
    {
        await _unitOfWork.BeginTransactionAsync();

        try
        {
        var booking = await _bookingRepository.GetByIdAsync(bookingId, cancellationToken);

        if (booking == null)
        {
            return OperationResult<WalletTransactionDto>.Failure("Booking not found");
        }

        var customer = await _userRepository.GetWithCustomerProfileByIdAsync(
            userId,
            cancellationToken
        );

        if (customer?.CustomerProfile == null)
        {
            return OperationResult<WalletTransactionDto>.Failure("Customer profile not found");
        }

        if (booking.CustomerProfileId != customer.CustomerProfile.Id)
        {
            return OperationResult<WalletTransactionDto>.Failure("Forbidden");
        }

        if (booking.FinalPrice == null || booking.FinalPrice <= 0)
        {
            return OperationResult<WalletTransactionDto>.Failure("Invalid booking price");
        }

        var existedOrder = await _paymentOrderRepository.GetBookingPaymentOrderAsync(
            bookingId,
            cancellationToken
        );

        if (existedOrder != null && existedOrder.Status == PaymentStatus.Paid)
        {
            return OperationResult<WalletTransactionDto>.Failure("Booking already paid");
        }

        var wallet = await _walletRepository.GetByUserIdAsync(
            userId,
            WalletOwnerType.Customer,
            cancellationToken
        );

        if (wallet == null)
        {
            return OperationResult<WalletTransactionDto>.Failure("Wallet not found");
        }

        var amount = booking.FinalPrice.Value;

        if (wallet.Balance < amount)
        {
            return OperationResult<WalletTransactionDto>.Failure("Insufficient balance");
        }

            // =========================
            // CREATE PAYMENT ORDER FIRST
            // =========================

            PaymentOrder order;

            if (existedOrder != null)
            {
                order = existedOrder;

                order.Method = PaymentMethod.Wallet;
                order.Status = PaymentStatus.Paid;
                order.PaidAt = DateTime.UtcNow;

                _paymentOrderRepository.Update(order);
            }
            else
            {
                order = new PaymentOrder
                {
                    BookingId = booking.Id,
                    UserId = userId,

                    Amount = amount,
                    DiscountAmount = 0,
                    FinalAmount = amount,

                    Method = PaymentMethod.Wallet,
                    Type = PaymentOrderType.BookingPayment,

                    Status = PaymentStatus.Paid,
                    PaidAt = DateTime.UtcNow,
                };

                await _paymentOrderRepository.AddAsync(order, cancellationToken);
            }

            // SAVE TO GET ORDER ID
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // =========================
            // UPDATE WALLET
            // =========================

            var before = wallet.Balance;

            wallet.Balance -= amount;

            wallet.LifetimeSpent += amount;

            // =========================
            // CREATE WALLET TRANSACTION
            // =========================

            var tx = new WalletTransaction
            {
                WalletId = wallet.Id,

                PaymentOrderId = order.Id,

                Type = WalletTransactionType.Payment,
                Direction = WalletDirection.Debit,

                Amount = amount,

                BalanceBefore = before,
                BalanceAfter = wallet.Balance,

                ReferenceId = booking.Id.ToString(),

                Status = TransactionStatus.Success,
            };

            await _walletTransactionRepository.AddAsync(tx, cancellationToken);

            _walletRepository.Update(wallet);

            // =========================
            // CONFIRM BOOKING PAYMENT
            // =========================

            await _bookingService.ConfirmPaymentAsync(booking.Id, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _unitOfWork.CommitTransactionAsync();

            return OperationResult<WalletTransactionDto>.Success(
                new WalletTransactionDto
                {
                    Id = tx.Id,
                    Type = tx.Type.ToString(),
                    Direction = tx.Direction.ToString(),
                    Amount = tx.Amount,
                    BalanceBefore = tx.BalanceBefore,
                    BalanceAfter = tx.BalanceAfter,
                    Status = tx.Status.ToString(),
                    CreatedDate = tx.CreatedDate,
                },
                "Booking paid successfully"
            );
        }
        catch (DbUpdateConcurrencyException)
        {
            await _unitOfWork.RollbackTransactionAsync();

            return OperationResult<WalletTransactionDto>.Failure("Wallet conflict, retry again");
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }


    public async Task<OperationResult<WalletTransactionDto>> RefundAsync(
        Guid userId,
        long amount,
        string referenceId,
        CancellationToken cancellationToken
    )
    {
        var exists = await _walletTransactionRepository.ExistsAsync(
            x => x.ReferenceId == referenceId && x.Type == WalletTransactionType.Refund,
            cancellationToken
        );

        if (exists)
            return OperationResult<WalletTransactionDto>.Failure("Already refunded");
        await _unitOfWork.BeginTransactionAsync();

        try
        {
        var wallet = await _walletRepository.GetByUserIdAsync(
            userId,
            WalletOwnerType.Customer,
            cancellationToken
        );

        if (wallet == null)
            return OperationResult<WalletTransactionDto>.Failure("Wallet not found");

            var before = wallet.Balance;

            wallet.Balance += amount;

            var tx = new WalletTransaction
            {
                WalletId = wallet.Id,
                Type = WalletTransactionType.Refund,
                Direction = WalletDirection.Credit,
                Amount = amount,
                BalanceBefore = before,
                BalanceAfter = wallet.Balance,
                ReferenceId = referenceId,
                Status = TransactionStatus.Success,
            };

            await _walletTransactionRepository.AddAsync(tx, cancellationToken);
            _walletRepository.Update(wallet);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync();

            return OperationResult<WalletTransactionDto>.Success(
                new WalletTransactionDto
                {
                    Id = tx.Id,
                    Type = tx.Type.ToString(),
                    Direction = tx.Direction.ToString(),
                    Amount = tx.Amount,
                    BalanceBefore = tx.BalanceBefore,
                    BalanceAfter = tx.BalanceAfter,
                    Status = tx.Status.ToString(),
                    CreatedDate = tx.CreatedDate,
                },
                "Refund success"
            );
        }
        catch (DbUpdateConcurrencyException)
        {
            await _unitOfWork.RollbackTransactionAsync();
            return OperationResult<WalletTransactionDto>.Failure("Wallet conflict, retry again");
        }
    }

    public async Task<OperationResult<WalletTransactionDto>> WithdrawAsync(
        Guid userId,
        long amount,
        string referenceId,
        CancellationToken cancellationToken
    )
    {
        await _unitOfWork.BeginTransactionAsync();

        try
        {
        var wallet = await _walletRepository.GetByUserIdAsync(
            userId,
            WalletOwnerType.Worker,
            cancellationToken
        );

        if (wallet == null)
            return OperationResult<WalletTransactionDto>.Failure("Wallet not found");

        // Chỉ kiểm tra và trừ trên Balance thu nhập, TUYỆT ĐỐI không đụng vào LockedBalance (tiền cọc)
        if (wallet.Balance < amount)
            return OperationResult<WalletTransactionDto>.Failure("Insufficient balance");

            var before = wallet.Balance;

            // 🔥 HOLD MONEY (giữ logic đơn giản MVP)
            wallet.Balance -= amount;

            wallet.LifetimeSpent += amount;

            var tx = new WalletTransaction
            {
                WalletId = wallet.Id,
                Type = WalletTransactionType.Withdrawal,
                Direction = WalletDirection.Debit,
                Amount = amount,
                BalanceBefore = before,
                BalanceAfter = wallet.Balance,
                ReferenceId = referenceId,
                Status = TransactionStatus.Pending,
            };

            await _walletTransactionRepository.AddAsync(tx, cancellationToken);
            _walletRepository.Update(wallet);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync();

            return OperationResult<WalletTransactionDto>.Success(
                new WalletTransactionDto
                {
                    Id = tx.Id,
                    Type = tx.Type.ToString(),
                    Direction = tx.Direction.ToString(),
                    Amount = tx.Amount,
                    BalanceBefore = tx.BalanceBefore,
                    BalanceAfter = tx.BalanceAfter,
                    Status = tx.Status.ToString(),
                    CreatedDate = tx.CreatedDate,
                },
                "Withdraw requested"
            );
        }
        catch (DbUpdateConcurrencyException)
        {
            await _unitOfWork.RollbackTransactionAsync();
            return OperationResult<WalletTransactionDto>.Failure("Wallet conflict, retry again");
        }
    }

    // =============================
    // Security Deposit (Tiền cọc ký quỹ)
    // =============================

    public async Task<OperationResult> DepositLockAsync(
        Guid userId,
        long amount,
        string referenceId,
        CancellationToken cancellationToken
    )
    {
        await _unitOfWork.BeginTransactionAsync();

        try
        {
            var wallet = await _walletRepository.GetByUserIdAsync(
                userId,
                WalletOwnerType.Worker,
                cancellationToken
            );

            if (wallet == null)
                return OperationResult.Failure("Worker wallet not found");

            var lockedBefore = wallet.LockedBalance;

            wallet.LockedBalance += amount;

            var tx = new WalletTransaction
            {
                WalletId = wallet.Id,
                Type = WalletTransactionType.DepositLock,
                Direction = WalletDirection.Credit,
                Amount = amount,
                BalanceBefore = wallet.Balance,
                BalanceAfter = wallet.Balance, // Balance thu nhập không thay đổi
                LockedBalanceBefore = lockedBefore,
                LockedBalanceAfter = wallet.LockedBalance,
                ReferenceId = referenceId,
                Status = TransactionStatus.Success,
            };

            await _walletTransactionRepository.AddAsync(tx, cancellationToken);
            _walletRepository.Update(wallet);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync();

            return OperationResult.Success("Deposit locked successfully");
        }
        catch (DbUpdateConcurrencyException)
        {
            await _unitOfWork.RollbackTransactionAsync();
            return OperationResult.Failure("Wallet conflict, retry again");
        }
    }

    public async Task<OperationResult> DepositRefundAsync(
        Guid userId,
        long amount,
        string referenceId,
        CancellationToken cancellationToken
    )
    {
        await _unitOfWork.BeginTransactionAsync();

        try
        {
            var wallet = await _walletRepository.GetByUserIdAsync(
                userId,
                WalletOwnerType.Worker,
                cancellationToken
            );

            if (wallet == null)
                return OperationResult.Failure("Worker wallet not found");

            if (wallet.LockedBalance < amount)
                return OperationResult.Failure("Insufficient locked deposit balance");

            var lockedBefore = wallet.LockedBalance;

            wallet.LockedBalance -= amount;

            var tx = new WalletTransaction
            {
                WalletId = wallet.Id,
                Type = WalletTransactionType.DepositRefund,
                Direction = WalletDirection.Debit,
                Amount = amount,
                BalanceBefore = wallet.Balance,
                BalanceAfter = wallet.Balance, // Balance thu nhập không thay đổi
                LockedBalanceBefore = lockedBefore,
                LockedBalanceAfter = wallet.LockedBalance,
                ReferenceId = referenceId,
                Status = TransactionStatus.Success,
            };

            await _walletTransactionRepository.AddAsync(tx, cancellationToken);
            _walletRepository.Update(wallet);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync();

            return OperationResult.Success("Deposit refunded successfully");
        }
        catch (DbUpdateConcurrencyException)
        {
            await _unitOfWork.RollbackTransactionAsync();
            return OperationResult.Failure("Wallet conflict, retry again");
        }
    }
}
