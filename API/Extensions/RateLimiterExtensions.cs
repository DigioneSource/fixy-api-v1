using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Application.Common;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Extensions
{
    public static class RateLimiterExtensions
    {
        public const string OtpSendPolicy = "OtpSendPolicy";
        public const string OtpVerifyPolicy = "OtpVerifyPolicy";
        public const string LoginPolicy = "LoginPolicy";
        public const string PasswordResetPolicy = "PasswordResetPolicy";

        public static IServiceCollection AddCustomRateLimiter(this IServiceCollection services)
        {
            services.AddRateLimiter(options =>
            {
                // Return 429 Too Many Requests when rate limit is exceeded
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.OnRejected = async (context, cancellationToken) =>
                {
                    var response = context.HttpContext.Response;
                    response.StatusCode = StatusCodes.Status429TooManyRequests;

                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds))
                            .ToString(CultureInfo.InvariantCulture);
                    }

                    await response.WriteAsJsonAsync(
                        OperationResult.Failure(
                            "Bạn thao tác quá nhiều lần, vui lòng thử lại sau ít phút"
                        ),
                        cancellationToken
                    );
                };

                // Register rate limiting policies
                ConfigureBookingDraftPolicy(options);

                AddIpFixedWindowPolicy(options, OtpSendPolicy, 5, TimeSpan.FromMinutes(15));
                AddIpFixedWindowPolicy(options, OtpVerifyPolicy, 10, TimeSpan.FromMinutes(5));
                AddIpFixedWindowPolicy(options, LoginPolicy, 10, TimeSpan.FromMinutes(5));
                AddIpFixedWindowPolicy(options, PasswordResetPolicy, 5, TimeSpan.FromMinutes(15));
            });

            return services;
        }

        /// <summary>
        /// Lấy IP thật của client. Máy chủ đứng sau Cloudflare nên ưu tiên CF-Connecting-IP;
        /// nếu không có thì dùng RemoteIpAddress (đã được ForwardedHeaders xử lý X-Forwarded-For).
        /// Lưu ý: cổng gốc chỉ nên nhận kết nối từ Cloudflare/tunnel, nếu không header này có thể bị giả.
        /// </summary>
        public static string GetClientIp(HttpContext context)
        {
            var cfIp = context.Request.Headers["CF-Connecting-IP"].ToString();

            if (!string.IsNullOrWhiteSpace(cfIp) && IPAddress.TryParse(cfIp.Trim(), out var parsed))
            {
                return parsed.ToString();
            }

            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        private static void AddIpFixedWindowPolicy(
            RateLimiterOptions options,
            string policyName,
            int permitLimit,
            TimeSpan window
        )
        {
            options.AddPolicy(policyName, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientIp(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = window,
                        QueueLimit = 0
                    }));
        }

        /// <summary>
        /// Limits booking draft creation to 5 requests per minute per user.
        /// Prevents spam creation of drafts that consume Redis cache resources.
        /// </summary>
        private static void ConfigureBookingDraftPolicy(RateLimiterOptions options)
        {
            options.AddPolicy("BookingDraftPolicy", context =>
            {
                var userId = context.User.Identity?.Name ?? "anonymous";

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: userId,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
            });
        }
    }
}
