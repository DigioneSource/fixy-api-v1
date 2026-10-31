using Application.Common;
using Application.DTOs.Media;
using Domain.Enum;

namespace Application.Interfaces.Services.Media
{
    public interface IMediaService
    {
        Task<OperationResult<List<MediaDto>>> UploadMediaAsync(
            UploadMediaFormDto request,
            CancellationToken cancellationToken = default
        );
        Task<OperationResult<MediaDto>> GetMediaByIdAsync(
            Guid mediaId,
            Guid requesterId,
            bool requesterIsAdmin,
            CancellationToken cancellationToken = default
        );
        Task<OperationResult<List<MediaDto>>> GetMediaByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken = default
        );
        Task<OperationResult<List<MediaDto>>> GetMyMediaAsync(
            MediaCategory? category = null,
            MediaOwnerType? ownerType = null,
            CancellationToken cancellationToken = default
        );
    }
}
