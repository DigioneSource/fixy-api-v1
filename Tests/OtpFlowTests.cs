using Application.Common;
using Application.DTOs.Auth;
using Application.Settings;
using Domain.Entity;
using Domain.Enum;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Repositories;
using Infrastructure.Services.Auth;
using Infrastructure.Services.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests
{
    public class OtpFlowTests
    {
        private const string Target = "khach@tayzen.vn";

        private static AppDbContext NewContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        private static OtpService NewOtpService(AppDbContext ctx)
        {
            // VerifyOtpAsync chỉ dùng repository OTP và UnitOfWork
            return new OtpService(
                new UserRepository(ctx),
                new UserOtpRepository(ctx),
                new UnitOfWork(ctx),
                null!,
                null!,
                null!
            );
        }

        private static AuthService NewAuthService(AppDbContext ctx)
        {
            return new AuthService(
                new UserRepository(ctx),
                null!,
                new UserOtpRepository(ctx),
                null!,
                null!,
                new RefreshTokenRepository(ctx),
                null!,
                new UnitOfWork(ctx),
                new PasswordHasher(),
                null!,
                Options.Create(new GoogleSettings()),
                Options.Create(new FacebookSettings()),
                Options.Create(new JwtSettings()),
                null!
            );
        }

        private static async Task SeedOtpAsync(AppDbContext ctx, EmailPurpose purpose)
        {
            ctx.UserOtps.Add(
                new UserOtp
                {
                    Target = Target,
                    OtpCode = "123456",
                    Purpose = purpose,
                    ExpiryDate = DateTime.UtcNow.AddMinutes(5),
                }
            );
            await ctx.SaveChangesAsync();
        }

        private static string? ExtractToken(OperationResult result) =>
            (result.Data as VerifyOtpResponseDto)?.VerifyToken;

        [Fact]
        public async Task NhapSai5Lan_KhoaMa_DungMaDungCungBiTuChoi()
        {
            using var ctx = NewContext();
            await SeedOtpAsync(ctx, EmailPurpose.ForgotPassword);
            var otpService = NewOtpService(ctx);

            for (var i = 1; i <= 4; i++)
            {
                var wrong = await otpService.VerifyOtpAsync(Target, "000000", default);
                Assert.False(wrong.IsSuccess);
            }

            var fifth = await otpService.VerifyOtpAsync(Target, "000000", default);
            Assert.False(fifth.IsSuccess);
            Assert.Contains("quá 5 lần", fifth.Message);

            var otp = await ctx.UserOtps.SingleAsync();
            Assert.True(otp.IsUsed);
            Assert.Equal(5, otp.FailedAttempts);

            var correctAfterLock = await otpService.VerifyOtpAsync(Target, "123456", default);
            Assert.False(correctAfterLock.IsSuccess);
        }

        [Fact]
        public async Task MaDung_TraVerifyToken_LuuHash()
        {
            using var ctx = NewContext();
            await SeedOtpAsync(ctx, EmailPurpose.ForgotPassword);
            var otpService = NewOtpService(ctx);

            var result = await otpService.VerifyOtpAsync(Target, "123456", default);

            Assert.True(result.IsSuccess);
            var token = ExtractToken(result);
            Assert.False(string.IsNullOrEmpty(token));

            var otp = await ctx.UserOtps.SingleAsync();
            Assert.True(otp.IsVerified);
            Assert.NotNull(otp.VerifyTokenHash);
            Assert.NotEqual(token, otp.VerifyTokenHash);
        }

        [Fact]
        public async Task VerifyToken_ChiDungMotLan_VaThuHoiRefreshToken()
        {
            using var ctx = NewContext();
            var user = new User
            {
                FullName = "Khach",
                Email = Target,
                PasswordHash = new PasswordHasher().HashPassword("old-pass"),
            };
            ctx.Users.Add(user);
            ctx.RefreshTokens.Add(
                new RefreshToken
                {
                    UserId = user.Id,
                    TokenHash = "abc",
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                }
            );
            await ctx.SaveChangesAsync();
            await SeedOtpAsync(ctx, EmailPurpose.ForgotPassword);

            var token = ExtractToken(
                await NewOtpService(ctx).VerifyOtpAsync(Target, "123456", default)
            );
            var auth = NewAuthService(ctx);

            var first = await auth.ResetPasswordAsync(
                new ResetPasswordRequestDto
                {
                    Target = Target,
                    NewPassword = "new-pass-1",
                    VerifyToken = token!,
                },
                default
            );
            Assert.True(first.IsSuccess);
            Assert.All(await ctx.RefreshTokens.ToListAsync(), t => Assert.True(t.IsRevoked));

            var second = await auth.ResetPasswordAsync(
                new ResetPasswordRequestDto
                {
                    Target = Target,
                    NewPassword = "new-pass-2",
                    VerifyToken = token!,
                },
                default
            );
            Assert.False(second.IsSuccess);
        }

        [Fact]
        public async Task ResetPassword_ThieuHoacSaiToken_HoacSaiMucDich_BiTuChoi()
        {
            using var ctx = NewContext();
            ctx.Users.Add(
                new User
                {
                    FullName = "Khach",
                    Email = Target,
                    PasswordHash = "x",
                }
            );
            await ctx.SaveChangesAsync();
            // OTP cấp cho mục đích Register, không được dùng để đặt lại mật khẩu
            await SeedOtpAsync(ctx, EmailPurpose.Register);

            var token = ExtractToken(
                await NewOtpService(ctx).VerifyOtpAsync(Target, "123456", default)
            );
            var auth = NewAuthService(ctx);

            foreach (var t in new[] { "", "sai-token", token! })
            {
                var result = await auth.ResetPasswordAsync(
                    new ResetPasswordRequestDto
                    {
                        Target = Target,
                        NewPassword = "new-pass",
                        VerifyToken = t,
                    },
                    default
                );
                Assert.False(result.IsSuccess);
            }
        }
    }
}
