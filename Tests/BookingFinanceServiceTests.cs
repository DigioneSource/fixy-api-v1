using Application.DTOs.Booking;
using Application.Settings;
using Domain.Entity;
using Domain.Enum;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Infrastructure.Services.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests;

/// <summary>Ký quỹ đơn tiền mặt + quyết toán ví KTV khi hoàn thành (CEO 31/10/2026).</summary>
public class BookingFinanceServiceTests
{
    private static readonly Guid WorkerUserId = Guid.NewGuid();

    private static AppDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    private static BookingFinanceService NewService(AppDbContext ctx) =>
        new(
            new WalletRepository(ctx),
            new WalletTransactionRepository(ctx),
            new BookingVoucherRepository(ctx),
            Options.Create(new CommissionSettings()),
            NullLogger<BookingFinanceService>.Instance
        );

    private static async Task<Wallet> SeedWalletAsync(AppDbContext ctx, long balance)
    {
        var wallet = new Wallet
        {
            UserId = WorkerUserId,
            OwnerType = WalletOwnerType.Worker,
            Balance = balance,
            RowVersion = new byte[8],
            CreatedAt = DateTime.UtcNow,
        };
        ctx.Wallets.Add(wallet);
        await ctx.SaveChangesAsync();
        return wallet;
    }

    private static Booking NewBooking(long price, PaymentMethod method, PaymentStatus status) =>
        new()
        {
            Id = Guid.NewGuid(),
            EstimatedPrice = price,
            FinalPrice = price,
            Status = BookingStatus.Pending,
            WorkerProfile = new WorkerProfile { UserId = WorkerUserId },
            PaymentOrder = new PaymentOrder
            {
                Id = Guid.NewGuid(),
                Method = method,
                Status = status,
                Amount = price,
                FinalAmount = price,
            },
        };

    [Fact]
    public async Task NhanDonTienMat_SoDuDuoi40PhanTram_TuChoiVaKhongTruTien()
    {
        await using var ctx = NewContext();
        var wallet = await SeedWalletAsync(ctx, 199_000);
        var booking = NewBooking(500_000, PaymentMethod.Cash, PaymentStatus.Pending);

        var result = await NewService(ctx).HoldCashPlatformFeeAsync(booking, WorkerUserId);
        await ctx.SaveChangesAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("200.000", result.Message);
        var shortfall = Assert.IsType<CashDepositShortfallDto>(result.Data);
        Assert.Equal(200_000, shortfall.RequiredBalance);
        Assert.Equal(199_000, shortfall.CurrentBalance);
        Assert.Equal(1_000, shortfall.Shortfall);
        Assert.Equal(199_000, wallet.Balance);
        Assert.Null(booking.HeldPlatformFee);
        Assert.Empty(ctx.WalletTransactions);
    }

    [Fact]
    public async Task NhanDonTienMat_KhongCoVi_TuChoi()
    {
        await using var ctx = NewContext();
        var booking = NewBooking(500_000, PaymentMethod.Cash, PaymentStatus.Pending);

        var result = await NewService(ctx).HoldCashPlatformFeeAsync(booking, WorkerUserId);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task NhanDonTienMat_DuSoDu_TamGiuPhiCongVat()
    {
        await using var ctx = NewContext();
        var wallet = await SeedWalletAsync(ctx, 200_000);
        var booking = NewBooking(500_000, PaymentMethod.Cash, PaymentStatus.Pending);

        var result = await NewService(ctx).HoldCashPlatformFeeAsync(booking, WorkerUserId);
        await ctx.SaveChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(165_000, booking.HeldPlatformFee);
        Assert.Equal(35_000, wallet.Balance);
        var tx = Assert.Single(ctx.WalletTransactions);
        Assert.Equal(WalletTransactionType.PlatformFeeHold, tx.Type);
        Assert.Equal(WalletDirection.Debit, tx.Direction);
        Assert.Equal(TransactionStatus.Pending, tx.Status);
        Assert.Equal(165_000, tx.Amount);
    }

    [Fact]
    public async Task HuyDonTienMat_HoanKhoanTamGiu()
    {
        await using var ctx = NewContext();
        var wallet = await SeedWalletAsync(ctx, 300_000);
        var booking = NewBooking(500_000, PaymentMethod.Cash, PaymentStatus.Pending);
        var service = NewService(ctx);
        await service.HoldCashPlatformFeeAsync(booking, WorkerUserId);
        await ctx.SaveChangesAsync();

        await service.ReleaseCashPlatformFeeAsync(booking, WorkerUserId);
        await ctx.SaveChangesAsync();

        Assert.Equal(300_000, wallet.Balance);
        Assert.Equal(0, booking.HeldPlatformFee);
        Assert.Contains(
            ctx.WalletTransactions,
            x => x.Type == WalletTransactionType.PlatformFeeHold && x.Status == TransactionStatus.Reversed
        );
        Assert.Contains(ctx.WalletTransactions, x => x.Type == WalletTransactionType.PlatformFeeRelease);
    }

    [Fact]
    public async Task HoanThanhDonTienMat_KhoanGiuThanhPhiChinhThuc()
    {
        await using var ctx = NewContext();
        var wallet = await SeedWalletAsync(ctx, 300_000);
        var booking = NewBooking(500_000, PaymentMethod.Cash, PaymentStatus.Pending);
        var service = NewService(ctx);
        await service.HoldCashPlatformFeeAsync(booking, WorkerUserId);
        await ctx.SaveChangesAsync();

        booking.PaymentOrder!.Status = PaymentStatus.Paid;
        await service.SettleCompletedBookingAsync(booking);
        await ctx.SaveChangesAsync();

        Assert.Equal(150_000, booking.PlatformFee);
        Assert.Equal(15_000, booking.PlatformFeeVat);
        Assert.Equal(335_000, booking.WorkerNet);
        Assert.Equal(135_000, wallet.Balance);
        var tx = Assert.Single(ctx.WalletTransactions);
        Assert.Equal(WalletTransactionType.Fee, tx.Type);
        Assert.Equal(TransactionStatus.Success, tx.Status);
    }

    [Fact]
    public async Task HoanThanhDonTraTruoc_CongWorkerNetVaoViKtv()
    {
        await using var ctx = NewContext();
        var wallet = await SeedWalletAsync(ctx, 0);
        var booking = NewBooking(500_000, PaymentMethod.PayOS, PaymentStatus.Paid);

        await NewService(ctx).SettleCompletedBookingAsync(booking);
        await ctx.SaveChangesAsync();

        Assert.Equal(335_000, wallet.Balance);
        var tx = Assert.Single(ctx.WalletTransactions);
        Assert.Equal(WalletTransactionType.BookingIncome, tx.Type);
        Assert.Equal(165_000, tx.PlatformFee);
    }

    [Fact]
    public async Task HoanThanhDonTraTruocCoVoucher_PhiTinhTrenGiaGoc_KtvKhongChiuVoucher()
    {
        await using var ctx = NewContext();
        var wallet = await SeedWalletAsync(ctx, 0);
        var booking = NewBooking(500_000, PaymentMethod.Vnpay, PaymentStatus.Paid);
        booking.FinalPrice = 400_000; // voucher 100k đã trừ vào FinalPrice
        booking.BookingVoucher = new BookingVoucher { DiscountAmount = 100_000 };

        await NewService(ctx).SettleCompletedBookingAsync(booking);
        await ctx.SaveChangesAsync();

        Assert.Equal(150_000, booking.PlatformFee);
        Assert.Equal(335_000, booking.WorkerNet);
        Assert.Equal(335_000, wallet.Balance);
    }
}
