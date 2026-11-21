using System.Net;
using System.Text.Json;
using Application.Settings;
using Infrastructure.Services.Sms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests
{
    /// <summary>OTP qua trạm ZNS của ERP DIGIFNB (21/11/2026).</summary>
    public class ZnsRelaySmsTests
    {
        private sealed class CaptureHandler : HttpMessageHandler
        {
            public HttpRequestMessage? Request;
            public string? Body;
            public HttpStatusCode Status = HttpStatusCode.OK;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Request = request;
                Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
                return new HttpResponseMessage(Status) { Content = new StringContent("{\"ok\":true}") };
            }
        }

        private sealed class Factory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _h;
            public Factory(HttpMessageHandler h) => _h = h;
            public HttpClient CreateClient(string name) => new(_h, disposeHandler: false);
        }

        private static (SmsService svc, CaptureHandler h) Make(SmsSettings s)
        {
            var h = new CaptureHandler();
            return (new SmsService(Options.Create(s), NullLogger<SmsService>.Instance, new Factory(h)), h);
        }

        private static SmsSettings Relay() => new()
        {
            UseMock = false, Provider = "ZnsRelay", BaseUrl = "https://erp.example/api/internal/zns/otp",
            ApiKey = "khoa-tram", SenderId = "TAYZEN"
        };

        [Fact]
        public async Task ZnsRelay_GuiMaVaKhoaDungCho()
        {
            var (svc, h) = Make(Relay());
            var ok = await svc.SendOtpAsync("0905123456", "123456", "noi dung sms");

            Assert.True(ok);
            Assert.Equal("https://erp.example/api/internal/zns/otp", h.Request!.RequestUri!.ToString());
            Assert.Equal("khoa-tram", h.Request.Headers.GetValues("X-Relay-Key").Single());
            using var doc = JsonDocument.Parse(h.Body!);
            Assert.Equal("tayzen", doc.RootElement.GetProperty("app").GetString());
            Assert.Equal("0905123456", doc.RootElement.GetProperty("phone").GetString());
            Assert.Equal("123456", doc.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain("noi dung", h.Body);
        }

        [Fact]
        public async Task ZnsRelay_TramLoiThiTraFalse()
        {
            var (svc, h) = Make(Relay());
            h.Status = HttpStatusCode.BadGateway;
            Assert.False(await svc.SendOtpAsync("0905123456", "123456", "x"));
        }

        [Fact]
        public async Task ZnsRelay_ThieuCauHinhThiKhongGoi()
        {
            var s = Relay(); s.ApiKey = "";
            var (svc, h) = Make(s);
            Assert.False(await svc.SendOtpAsync("0905123456", "123456", "x"));
            Assert.Null(h.Request);
        }

        [Fact]
        public async Task Mock_KhongGoiTram()
        {
            var s = Relay(); s.UseMock = true;
            var (svc, h) = Make(s);
            Assert.True(await svc.SendOtpAsync("0905123456", "123456", "x"));
            Assert.Null(h.Request);
        }
    }
}
