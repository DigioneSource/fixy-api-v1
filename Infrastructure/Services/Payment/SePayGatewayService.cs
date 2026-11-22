using System.Text.RegularExpressions;
using Application.Interfaces.Services.Payment;
using Application.Settings;
using Domain.Entity;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.Payment;

/// <summary>
/// "Cổng" chuyển khoản QR qua SePay: không gọi API nào — sinh mã nội dung chuyển khoản
/// (TAYZEN + số) gắn vào đơn và trả link ảnh VietQR. Tiền về thì SePay gọi webhook
/// /api/payment/webhook/sepay (TransferType = "in") → PaymentService.HandleSePayIncomingAsync.
/// </summary>
public class SePayGatewayService : IPaymentGateway
{
    private readonly SePaySettings _settings;

    public SePayGatewayService(IOptions<SePaySettings> options)
    {
        _settings = options.Value;
    }

    public Task<string> CreatePaymentUrlAsync(PaymentOrder order, CancellationToken cancellationToken)
    {
        if (!_settings.IsConfigured)
            throw new InvalidOperationException("Chuyển khoản QR chưa được cấu hình (SePay:BankCode/AccountNumber).");

        // 10 chữ số, đủ khác nhau giữa các đơn; lưu ở GatewayOrderCode để webhook tra lại.
        order.GatewayOrderCode = NewOrderCode();
        return Task.FromResult(BuildQrUrl(_settings, order.FinalAmount, TransferContent(_settings, order.GatewayOrderCode.Value)));
    }

    public bool VerifySignature(Dictionary<string, string> response) => false;

    /// <summary>So token webhook ("Authorization: Apikey xxx") thời gian hằng; token trống = luôn sai.</summary>
    public static bool IsValidWebhookToken(string? expected, string? authorizationHeader)
    {
        expected = expected?.Trim();
        if (string.IsNullOrEmpty(expected)) return false;
        var received = (authorizationHeader ?? string.Empty).Trim();
        if (received.StartsWith("Apikey ", StringComparison.OrdinalIgnoreCase)) received = received[7..].Trim();
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(received), System.Text.Encoding.UTF8.GetBytes(expected));
    }

    public static long NewOrderCode() =>
        1_000_000_000L + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 9_000_000_000L);

    public static string TransferContent(SePaySettings s, long code)
    {
        var tz = $"{(string.IsNullOrWhiteSpace(s.CodePrefix) ? "TAYZEN" : s.CodePrefix.Trim().ToUpperInvariant())} {code}";
        return string.IsNullOrWhiteSpace(s.ContentPrefix) ? tz : $"{s.ContentPrefix.Trim().ToUpperInvariant()} {tz}";
    }

    /// <summary>
    /// Link ảnh VietQR của SePay. App đọc lại các tham số (acc, bank, amount, des, name)
    /// để hiện thông tin chuyển khoản thủ công bên cạnh mã QR.
    /// </summary>
    public static string BuildQrUrl(SePaySettings s, long amount, string content) =>
        "https://qr.sepay.vn/img"
        + $"?acc={Uri.EscapeDataString(s.AccountNumber.Trim())}"
        + $"&bank={Uri.EscapeDataString(s.BankCode.Trim())}"
        + $"&amount={amount}"
        + $"&des={Uri.EscapeDataString(content)}"
        + "&template=compact"
        + $"&name={Uri.EscapeDataString(s.AccountName.Trim())}";

    /// <summary>
    /// Tìm mã đơn trong nội dung chuyển khoản. Ngân hàng hay chèn thêm chữ, bỏ dấu cách hoặc
    /// đổi chữ thường — nên so khớp không phân biệt hoa thường, cho phép có dấu cách/gạch giữa tiền tố và số.
    /// </summary>
    public static long? TryParseOrderCode(string? content, string? prefix)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        // CEO duyệt bảng mã 22/11: phát TAYZEN; TZ (mã cũ khách đang cầm) và tiền tố cấu hình vẫn nhận.
        var names = new[] { string.IsNullOrWhiteSpace(prefix) ? "TAYZEN" : prefix.Trim(), "TAYZEN", "TZ" }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Regex.Escape);
        var p = $"(?:{string.Join("|", names)})";
        var m = Regex.Match(content, $@"(?<![A-Za-z0-9]){p}[\s\-_.]?(\d{{10}})(?!\d)", RegexOptions.IgnoreCase);
        if (!m.Success)
            m = Regex.Match(content, $@"{p}[\s\-_.]?(\d{{10}})(?!\d)", RegexOptions.IgnoreCase);
        return m.Success && long.TryParse(m.Groups[1].Value, out var code) ? code : null;
    }
}
