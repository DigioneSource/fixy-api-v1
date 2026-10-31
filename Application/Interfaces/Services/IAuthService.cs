using Application.Common;
using Application.DTOs.Auth;

namespace Application.Interfaces.Services.Auth
{
    public interface IAuthService
    {
        Task<OperationResult<AuthResponseDto>> RegisterAsync(
            RegisterRequestDto request,
            CancellationToken cancellationToken
        );

        Task<OperationResult<AuthResponseDto>> LoginAsync(
            LoginRequestDto request,
            CancellationToken cancellationToken
        );
        Task<OperationResult<AuthResponseDto>> GoogleLoginAsync(
            GoogleLoginRequestDto requestDto,
            CancellationToken cancellationToken
        );
        Task<OperationResult<AuthResponseDto>> FacebookLoginAsync(FacebookLoginRequestDto requestDto,CancellationToken cancellationToken);
        Task<OperationResult<AuthResponseDto>> RefreshTokenAsync(
            string refreshToken,
            CancellationToken cancellationToken
        );
        Task<OperationResult> ChangePasswordAsync(
            Guid currentUserId,
            ChangePasswordRequestDto request,
            CancellationToken cancellationToken
        );
        Task<OperationResult> ResetPasswordAsync(
            ResetPasswordRequestDto request,
            CancellationToken cancellationToken
        );
    }
}
