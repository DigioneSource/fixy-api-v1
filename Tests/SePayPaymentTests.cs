using System.Reflection;
using Application.DTOs.Payment;
using Application.Interfaces.Services;
using Application.Interfaces.Services.Booking;
using Application.Settings;
using Domain.Entity;
using Domain.Enum;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Infrastructure.Services.Payment;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests
{
    /// <summary>Thu tiền chuyển khoản QR qua SePay (21/11/2026).</summary>
    public class SePayPaymentTests
    {
        /// <summary>Giả lập interface bất kỳ: ghi lại tên hàm được gọi, Task trả về hoàn tất.</summary>
        public class Fake<T> : DispatchProxy where T : class
        {
            public List<string> Calls { get; } = new();
            public static (T Proxy, Fake<T> Spy) Create()
            {
                var p = DispatchProxy.Create<T, Fake<T>>();
                return (p, (Fake<T>)(object)p);
            }
            protected override object? Invoke(MethodInfo? m, object?[]? args)
            {
                Calls.Add(m!.Name);
                var rt = m.ReturnType;
                if (rt == typeof(Task)) return Task.CompletedTask;
                if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    var inner = rt.GetGenericArguments()[0];
                    var val = inner.IsValueType ? Activator.CreateInstance(inner) : null;
                    return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, new[] { val });
                }
                return rt.IsValueType ? Activator.CreateInstance(rt) : null;
            }
        }

        private const string Token = "token-bi-mat-thu";

        private static SePaySettings Settings() => new()
        {
            BankCode = "MBBank", AccountNumber = "0123456789", AccountName = "CONG TY TNHH DIGI FNB",
            WebhookToken = Token, CodePrefix = "TAYZEN"
        };

        private static AppDbContext NewContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static (PaymentService svc, Fake<IBookingService> booking, Fake<IWalletService> wallet) NewService(AppDbContext ctx, SePaySettings? s = null)
        {
            var (booking, bookingSpy) = Fake<IBookingService>.Create();
            var (wallet, walletSpy) = Fake<IWalletService>.Create();
            var (notif, _) = Fake<INotificationService>.Create();
            var svc = new PaymentService(
                new PaymentOrderRepository(ctx), null!, null!, null!, wallet, new UnitOfWork(ctx),
                booking, notif, null!, NullLogger<PaymentService>.Instance, null!, null!,
                Options.Create(s ?? Settings()));
            return (svc, bookingSpy, walletSpy);
        }

        private static async Task<PaymentOrder> SeedOrderAsync(AppDbContext ctx, long code, long amount = 300_000)
        {
            var o = new PaymentOrder
            {
                UserId = Guid.NewGuid(), BookingId = Guid.NewGuid(), Amount = amount, FinalAmount = amount,
                Method = PaymentMethod.SePay, Status = PaymentStatus.Pending,
                Type = PaymentOrderType.BookingPayment, GatewayOrderCode = code,
            };
            ctx.PaymentOrders.Add(o);
            await ctx.SaveChangesAsync();
            return o;
        }

        private static SePayWebhookDto In(string content, long amount, long id = 1) => new()
        {
            Id = id, Gateway = "MBBank", AccountNumber = "0123456789", TransferType = "in",
            TransferAmount = amount, Content = content, ReferenceCode = "FT123"
        };

        [Theory]
        [InlineData("TZ1234567890", 1234567890L)]
        [InlineData("NGUYEN VAN A chuyen tien tz1234567890 FT2611", 1234567890L)]
        [InlineData("MBVCB.123.TZ 1234567890.CT", 1234567890L)]
        [InlineData("TZ-1234567890", 1234567890L)]
        [InlineData("SEVQR TZ1234567890", 1234567890L)]
        [InlineData("IBFT SEVQR TZ1234567890 FT26325", 1234567890L)]
        [InlineData("SEVQR TAYZEN 5271992867", 5271992867L)]
        [InlineData("sevqrtayzen5271992867", 5271992867L)]
        [InlineData("SEVQR TAYZEN5271992867 FT26326", 5271992867L)]
        [InlineData("TAYZEN52719928671", null)]
        [InlineData("TZ123456789", null)]
        [InlineData("TZ12345678901", null)]
        [InlineData("thanh toan don 1234567890", null)]
        [InlineData("", null)]
        public void TachMaDonTuNoiDungChuyenKhoan(string content, long? expected) =>
            Assert.Equal(expected, SePayGatewayService.TryParseOrderCode(content, "TZ"));

        [Theory]
        [InlineData("SEVQR TAYZEN 5271992867")]
        [InlineData("SEVQR TZ5271992867")]
        public void TienToMoi_VanNhanMaCu(string content) =>
            Assert.Equal(5271992867L, SePayGatewayService.TryParseOrderCode(content, "TAYZEN"));

        [Fact]
        public void TokenWebhook_TrongLaLuonSai()
        {
            Assert.True(SePayGatewayService.IsValidWebhookToken(Token, $"Apikey {Token}"));
            Assert.False(SePayGatewayService.IsValidWebhookToken(Token, "Apikey sai"));
            Assert.False(SePayGatewayService.IsValidWebhookToken("", "Apikey "));
            Assert.False(SePayGatewayService.IsValidWebhookToken(null, null));
        }

        [Fact]
        public async Task TaoLinkQr_GanMaDonVaNoiDung()
        {
            var order = new PaymentOrder { FinalAmount = 250_000 };
            var url = await new SePayGatewayService(Options.Create(Settings())).CreatePaymentUrlAsync(order, default);
            Assert.NotNull(order.GatewayOrderCode);
            Assert.InRange(order.GatewayOrderCode!.Value, 1_000_000_000L, 9_999_999_999L);
            Assert.StartsWith("https://qr.sepay.vn/img?acc=0123456789&bank=MBBank&amount=250000&des=SEVQR%20TAYZEN%20", url);
            Assert.Contains($"des=SEVQR%20TAYZEN%20{order.GatewayOrderCode}", url);
        }

        [Fact]
        public async Task ChuaCauHinhTaiKhoan_ThiBaoLoiCauHinh()
        {
            var s = Settings(); s.AccountNumber = "";
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new SePayGatewayService(Options.Create(s)).CreatePaymentUrlAsync(new PaymentOrder { FinalAmount = 1 }, default));
        }

        [Fact]
        public async Task TienVeDungMaDungSoTien_GhiNhanDaThanhToan()
        {
            using var ctx = NewContext();
            var order = await SeedOrderAsync(ctx, 1234567890);
            var (svc, booking, _) = NewService(ctx);

            var r = await svc.HandleSePayIncomingAsync(In("KH CK TZ1234567890", 300_000, 77), $"Apikey {Token}", default);

            Assert.True(r.IsSuccess);
            var saved = await ctx.PaymentOrders.FindAsync(order.Id);
            Assert.Equal(PaymentStatus.Paid, saved!.Status);
            Assert.Equal("77", saved.ExternalTransactionId);
            Assert.Contains("ConfirmPaymentAsync", booking.Calls);
        }

        [Fact]
        public async Task SaiToken_TuChoiKhongGhiNhan()
        {
            using var ctx = NewContext();
            var order = await SeedOrderAsync(ctx, 1234567890);
            var (svc, booking, _) = NewService(ctx);

            var r = await svc.HandleSePayIncomingAsync(In("TZ1234567890", 300_000), "Apikey sai", default);

            Assert.False(r.IsSuccess);
            Assert.Equal(PaymentStatus.Pending, (await ctx.PaymentOrders.FindAsync(order.Id))!.Status);
            Assert.Empty(booking.Calls);
        }

        [Fact]
        public async Task ChuaDatToken_TuChoi()
        {
            using var ctx = NewContext();
            await SeedOrderAsync(ctx, 1234567890);
            var s = Settings(); s.WebhookToken = "";
            var (svc, _, _) = NewService(ctx, s);
            Assert.False((await svc.HandleSePayIncomingAsync(In("TZ1234567890", 300_000), "Apikey ", default)).IsSuccess);
        }

        [Fact]
        public async Task SaiSoTien_KhongGhiNhan()
        {
            using var ctx = NewContext();
            var order = await SeedOrderAsync(ctx, 1234567890);
            var (svc, booking, _) = NewService(ctx);

            var r = await svc.HandleSePayIncomingAsync(In("TZ1234567890", 299_000), $"Apikey {Token}", default);

            Assert.True(r.IsSuccess); // trả 200 để SePay không gửi lại; đơn vẫn chờ
            Assert.Equal(PaymentStatus.Pending, (await ctx.PaymentOrders.FindAsync(order.Id))!.Status);
            Assert.Empty(booking.Calls);
        }

        [Fact]
        public async Task GuiLaiCungGiaoDich_KhongXuLyHaiLan()
        {
            using var ctx = NewContext();
            await SeedOrderAsync(ctx, 1234567890);
            var (svc, booking, _) = NewService(ctx);

            await svc.HandleSePayIncomingAsync(In("TZ1234567890", 300_000), $"Apikey {Token}", default);
            await svc.HandleSePayIncomingAsync(In("TZ1234567890", 300_000), $"Apikey {Token}", default);

            Assert.Single(booking.Calls, c => c == "ConfirmPaymentAsync");
        }

        [Fact]
        public async Task ChuyenKhoanKhongCoMaHoacMaLa_BoQua()
        {
            using var ctx = NewContext();
            var (svc, booking, _) = NewService(ctx);
            Assert.True((await svc.HandleSePayIncomingAsync(In("chuyen tien an trua", 50_000), $"Apikey {Token}", default)).IsSuccess);
            Assert.True((await svc.HandleSePayIncomingAsync(In("TZ9999999999", 50_000), $"Apikey {Token}", default)).IsSuccess);
            Assert.Empty(booking.Calls);
        }
    }
}
