using Application.Common;
using Application.Settings;
using Domain.Enum;
using Xunit;

namespace Tests;

/// <summary>Công thức tiền CEO chốt 31/10/2026: phí 30% + VAT 10%, ký quỹ 40%, phí huỷ 30% (70/30).</summary>
public class PlatformFeeCalculatorTests
{
    private static readonly CommissionSettings Settings = new();

    [Theory]
    // giá, phí, VAT, KTV nhận — khớp calcCommission() của app
    [InlineData(500_000, 150_000, 15_000, 335_000)]
    [InlineData(350_000, 105_000, 10_500, 234_500)]
    [InlineData(199_999, 60_000, 6_000, 133_999)]
    [InlineData(0, 0, 0, 0)]
    public void Calculate_PhiNenTang30PhanTramCongVat10PhanTram(
        long price,
        long fee,
        long vat,
        long workerNet
    )
    {
        var result = PlatformFeeCalculator.Calculate(price, Settings);

        Assert.Equal(price, result.ServicePrice);
        Assert.Equal(fee, result.PlatformFee);
        Assert.Equal(vat, result.PlatformFeeVat);
        Assert.Equal(workerNet, result.WorkerNet);
        Assert.Equal(fee + vat, result.TotalFee);
        Assert.Equal(price, result.PlatformFee + result.PlatformFeeVat + result.WorkerNet);
    }

    [Fact]
    public void Calculate_LamTronNuaLenNhuApp()
    {
        // 30% × 5 = 1,5 → 2 (Math.round của JS), VAT 10% × 2 = 0,2 → 0
        var result = PlatformFeeCalculator.Calculate(5, Settings);

        Assert.Equal(2, result.PlatformFee);
        Assert.Equal(0, result.PlatformFeeVat);
        Assert.Equal(3, result.WorkerNet);
    }

    [Fact]
    public void Calculate_DocTiLeTuCauHinh()
    {
        var custom = new CommissionSettings { PlatformFeeRate = 0.20m, VatRate = 0.08m };

        var result = PlatformFeeCalculator.Calculate(1_000_000, custom);

        Assert.Equal(200_000, result.PlatformFee);
        Assert.Equal(16_000, result.PlatformFeeVat);
        Assert.Equal(784_000, result.WorkerNet);
    }

    [Theory]
    [InlineData(500_000, 200_000)]
    [InlineData(350_000, 140_000)]
    [InlineData(333_333, 134_000)] // 133.333,2 → làm tròn lên nghìn
    [InlineData(0, 0)]
    public void RequiredCashDeposit_40PhanTramLamTronLenNghin(long price, long required)
    {
        Assert.Equal(required, PlatformFeeCalculator.RequiredCashDeposit(price, Settings));
    }

    [Theory]
    [InlineData(BookingStatus.Pending)]
    [InlineData(BookingStatus.PendingPayment)]
    [InlineData(BookingStatus.Matching)]
    [InlineData(BookingStatus.Confirmed)]
    public void CalculateCancellation_TruocKhiKtvXuatPhat_MienPhi(BookingStatus status)
    {
        var result = PlatformFeeCalculator.CalculateCancellation(status, 500_000, 500_000, Settings);

        Assert.Equal(0, result.CancellationFee);
        Assert.Equal(0, result.WorkerShare);
        Assert.Equal(0, result.PlatformShare);
        Assert.Equal(500_000, result.RefundAmount);
    }

    [Theory]
    [InlineData(BookingStatus.Traveling)]
    [InlineData(BookingStatus.Arrived)]
    public void CalculateCancellation_TuKhiKtvXuatPhat_Thu30PhanTramChia70Ktv(BookingStatus status)
    {
        var result = PlatformFeeCalculator.CalculateCancellation(status, 500_000, 500_000, Settings);

        Assert.Equal(150_000, result.CancellationFee);
        Assert.Equal(105_000, result.WorkerShare);
        Assert.Equal(45_000, result.PlatformShare);
        Assert.Equal(350_000, result.RefundAmount);
    }

    [Fact]
    public void CalculateCancellation_PhiTinhTrenGiaTruocVoucher_KhongVuotSoDaTra()
    {
        // Giá 500k, voucher 400k → khách trả 100k; phí 30% × 500k = 150k nhưng chỉ thu tối đa 100k.
        var result = PlatformFeeCalculator.CalculateCancellation(
            BookingStatus.Traveling,
            500_000,
            100_000,
            Settings
        );

        Assert.Equal(100_000, result.CancellationFee);
        Assert.Equal(70_000, result.WorkerShare);
        Assert.Equal(30_000, result.PlatformShare);
        Assert.Equal(0, result.RefundAmount);
    }

    [Fact]
    public void CalculateCancellation_DonTienMat_ChiGhiPhiPhaiThu()
    {
        var result = PlatformFeeCalculator.CalculateCancellation(
            BookingStatus.Arrived,
            500_000,
            0,
            Settings
        );

        Assert.Equal(150_000, result.CancellationFee);
        Assert.Equal(0, result.RefundAmount);
    }

    [Theory]
    [InlineData(BookingStatus.InProgress)]
    [InlineData(BookingStatus.Completed)]
    public void IsCancellationFeeApplicable_ChiTravelingVaArrived(BookingStatus status)
    {
        Assert.False(PlatformFeeCalculator.IsCancellationFeeApplicable(status));
        Assert.True(PlatformFeeCalculator.IsCancellationFeeApplicable(BookingStatus.Traveling));
        Assert.True(PlatformFeeCalculator.IsCancellationFeeApplicable(BookingStatus.Arrived));
    }
}
