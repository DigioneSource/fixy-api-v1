using System.Security.Claims;
using Application.DTOs.Auth;
using Application.Interfaces.Services.Auth;
using Application.Interfaces.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using API.Extensions;

namespace API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ApiController
{
    private readonly IOtpService _otpService;

    private readonly IAuthService _authService;

    public AuthController(IOtpService otpService, IAuthService authService)
    {
        _otpService = otpService;
        _authService = authService;
    }

    // =========================
    // OTP
    // =========================

    [HttpPost("otp/send")]
    [EnableRateLimiting(RateLimiterExtensions.OtpSendPolicy)]
    public async Task<IActionResult> SendOtp(
        [FromBody] SendOtpRequestDto request,
        CancellationToken cancellationToken
    )
    {
        var result = await _otpService.SendOtpAsync(
            request.Target,
            request.Purpose,
            cancellationToken
        );

        return HandleResult(result);
    }

    [HttpPost("otp/verify")]
    [EnableRateLimiting(RateLimiterExtensions.OtpVerifyPolicy)]
    public async Task<IActionResult> VerifyOtp(
        [FromBody] VerifyOtpRequestDto request,
        CancellationToken cancellationToken
    )
    {
        var result = await _otpService.VerifyOtpAsync(
            request.Target,
            request.OtpCode,
            cancellationToken
        );

        return HandleResult(result);
    }

    // =========================
    // REGISTER
    // =========================

    [HttpPost("register")]
    [EnableRateLimiting(RateLimiterExtensions.OtpVerifyPolicy)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequestDto request,
        CancellationToken cancellationToken
    )
    {
        var result = await _authService.RegisterAsync(request, cancellationToken);
        return HandleResult(result);
    }

    // =========================
    // LOGIN
    // =========================

    [HttpPost("login")]
    [EnableRateLimiting(RateLimiterExtensions.LoginPolicy)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequestDto request,
        CancellationToken cancellationToken
    )
    {
        var result = await _authService.LoginAsync(request, cancellationToken);
        return HandleResult(result);
    }

    // =========================
    // LOGIN GOOGLE
    // =========================

    [HttpPost("login/google")]
    [EnableRateLimiting(RateLimiterExtensions.LoginPolicy)]
    public async Task<IActionResult> LoginGoogle(
        [FromBody] GoogleLoginRequestDto request,
        CancellationToken cancellationToken
    )
    {
        var result = await _authService.GoogleLoginAsync(request, cancellationToken);
        return HandleResult(result);
    }

    // =========================
    // LOGIN FACEBOOK
    // =========================

    [HttpPost("login/facebook")]
    [EnableRateLimiting(RateLimiterExtensions.LoginPolicy)]
    public async Task<IActionResult> LoginFacebook([FromBody] FacebookLoginRequestDto request,CancellationToken cancellationToken)
    {
        var result = await _authService.FacebookLoginAsync(request, cancellationToken);
        return HandleResult(result);
    }

    // =========================
    // REFRESH TOKEN
    // =========================

    [HttpPost("token/refresh")]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenRequestDto request,
        CancellationToken cancellationToken
    )
    {
        var result = await _authService.RefreshTokenAsync(request.RefreshToken, cancellationToken);
        return HandleResult(result);
    }

    // =========================
    // CHANGE PASSWORD
    // =========================

    [Authorize]
    [HttpPost("password/change")]
    [EnableRateLimiting(RateLimiterExtensions.LoginPolicy)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequestDto request,
        CancellationToken cancellationToken
    )
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var currentUserId))
            return Unauthorized();

        var result = await _authService.ChangePasswordAsync(currentUserId, request, cancellationToken);
        return HandleResult(result);
    }

    // =========================
    // RESET PASSWORD
    // =========================
    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimiterExtensions.PasswordResetPolicy)]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequestDto request,
        CancellationToken cancellationToken
    )
    {
        var result = await _authService.ResetPasswordAsync(request, cancellationToken);
        return HandleResult(result);
    }
}
