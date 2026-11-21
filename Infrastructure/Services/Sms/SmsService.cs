using System.Text;
using System.Text.Json;
using Application.Interfaces.Services.Sms;
using Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.Sms
{
    public class SmsService : ISmsService
    {
        private readonly SmsSettings _settings;
        private readonly ILogger<SmsService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public SmsService(
            IOptions<SmsSettings> settings,
            ILogger<SmsService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _settings = settings.Value;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<bool> SendSmsAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
        {
            var normalizedPhone = NormalizePhoneNumber(phoneNumber);

            if (_settings.UseMock || string.Equals(_settings.Provider, "Mock", StringComparison.OrdinalIgnoreCase))
            {
                LogMockSms(normalizedPhone, message);
                return true;
            }

            try
            {
                return _settings.Provider.ToLower() switch
                {
                    "speedsms" => await SendViaSpeedSmsAsync(normalizedPhone, message, cancellationToken),
                    "twilio" => await SendViaTwilioAsync(normalizedPhone, message, cancellationToken),
                    _ => await SendViaGenericHttpAsync(normalizedPhone, message, cancellationToken)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SMS SERVICE ERROR] Thất bại khi gửi SMS tới {Phone}", normalizedPhone);
                return false;
            }
        }

        public async Task<bool> SendOtpAsync(string phoneNumber, string otpCode, string message, CancellationToken cancellationToken = default)
        {
            if (!_settings.UseMock && string.Equals(_settings.Provider, "ZnsRelay", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return await SendViaZnsRelayAsync(phoneNumber, otpCode, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ZNS RELAY ERROR] Thất bại khi gửi OTP tới {Phone}", MaskPhone(phoneNumber));
                    return false;
                }
            }

            return await SendSmsAsync(phoneNumber, message, cancellationToken);
        }

        /// <summary>
        /// Gửi OTP qua trạm ZNS của ERP DIGIFNB (POST /api/internal/zns/otp, header X-Relay-Key).
        /// ERP là nơi duy nhất giữ refresh token Zalo — không tự gọi Zalo từ đây.
        /// BaseUrl = địa chỉ trạm, ApiKey = khoá trạm, SenderId = tên app đăng ký ở trạm.
        /// </summary>
        private async Task<bool> SendViaZnsRelayAsync(string phone, string otpCode, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_settings.BaseUrl) || string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                _logger.LogError("[ZNS RELAY] Thiếu SmsSettings:BaseUrl hoặc SmsSettings:ApiKey");
                return false;
            }

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(Math.Max(_settings.TimeoutSeconds, 20));

            var payload = new
            {
                app = string.IsNullOrWhiteSpace(_settings.SenderId) ? "tayzen" : _settings.SenderId.Trim().ToLowerInvariant(),
                phone = phone.Trim(),
                code = otpCode
            };
            var request = new HttpRequestMessage(HttpMethod.Post, _settings.BaseUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Relay-Key", _settings.ApiKey);

            var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[ZNS RELAY] Đã gửi OTP qua Zalo tới {Phone}", MaskPhone(phone));
                return true;
            }

            // body chỉ có lý do + mã lỗi Zalo, không chứa mã OTP
            _logger.LogWarning("[ZNS RELAY] Trạm trả lỗi ({Status}) cho {Phone}: {Body}", (int)response.StatusCode, MaskPhone(phone), body);
            return false;
        }

        private static string MaskPhone(string phone)
        {
            var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
            return digits.Length >= 4 ? $"***{digits[^3..]}" : "***";
        }

        private void LogMockSms(string phone, string message)
        {
            _logger.LogInformation("\n======================================================\n" +
                                  "========== [SMS MOCK GATEWAY] ========================\n" +
                                  "  To:      {Phone}\n" +
                                  "  Message: {Message}\n" +
                                  "  Time:    {Time}\n" +
                                  "======================================================\n",
                                  phone, message, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"));
        }

        private string NormalizePhoneNumber(string phone)
        {
            phone = phone.Trim().Replace(" ", "").Replace("-", "");
            if (phone.StartsWith("0"))
            {
                return "+84" + phone.Substring(1);
            }
            if (!phone.StartsWith("+"))
            {
                return "+" + phone;
            }
            return phone;
        }

        private async Task<bool> SendViaSpeedSmsAsync(string phone, string message, CancellationToken ct)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);

            var requestUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl) ? "https://api.speedsms.vn/index.php/sms/send" : _settings.BaseUrl;

            var payload = new
            {
                to = new[] { phone },
                content = message,
                sms_type = 2,
                sender = _settings.SenderId
            };

            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            var authenticationHeaderValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authenticationHeaderValue);

            var response = await client.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[SMS SENT SUCCESS] Gửi SMS thành công tới {Phone}", phone);
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("[SMS SENT FAILED] SpeedSMS trả về lỗi ({Status}): {Error}", response.StatusCode, errorContent);
            return false;
        }

        private async Task<bool> SendViaTwilioAsync(string phone, string message, CancellationToken ct)
        {
            var client = _httpClientFactory.CreateClient();
            var requestUrl = $"https://api.twilio.com/2010-04-01/Accounts/{_settings.ApiKey}/Messages.json";

            var nvc = new List<KeyValuePair<string, string>>
            {
                new("To", phone),
                new("From", _settings.SenderId),
                new("Body", message)
            };

            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = new FormUrlEncodedContent(nvc)
            };

            var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:{_settings.ApiSecret}"));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);

            var response = await client.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[SMS SENT SUCCESS] Twilio gửi SMS thành công tới {Phone}", phone);
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("[SMS SENT FAILED] Twilio trả về lỗi ({Status}): {Error}", response.StatusCode, errorContent);
            return false;
        }

        private async Task<bool> SendViaGenericHttpAsync(string phone, string message, CancellationToken ct)
        {
            _logger.LogInformation("[GENERIC SMS] Mô phỏng gọi API cho {Phone}", phone);
            await Task.Delay(100, ct);
            return true;
        }
    }
}
