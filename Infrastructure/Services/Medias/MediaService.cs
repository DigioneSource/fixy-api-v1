using Application.Common;
using Application.Common.Interfaces;
using Application.DTOs.Media;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services.Media;
using Domain.Entity;
using Domain.Enum;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Medias
{
    public class MediaService : IMediaService
    {
        private readonly IBlobService _blobService;
        private readonly IMediaRepository _mediaRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<MediaService> _logger;

        public MediaService(
            IBlobService blobService,
            IMediaRepository mediaRepository,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ILogger<MediaService> logger
        )
        {
            _blobService = blobService ?? throw new ArgumentNullException(nameof(blobService));
            _mediaRepository = mediaRepository ?? throw new ArgumentNullException(nameof(mediaRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<OperationResult<List<MediaDto>>> UploadMediaAsync(
            UploadMediaFormDto request,
            CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrWhiteSpace(_currentUserService.UserId) || !Guid.TryParse(_currentUserService.UserId, out var uploadedById))
            {
                _logger.LogWarning("User ID not found in token");
                return OperationResult<List<MediaDto>>.Failure("User ID not found in token");
            }

            var uploadedMediaList = new List<Media>();
            var results = new List<MediaDto>();

            foreach (var file in request.Files)
            {
                if (file.Length > 0)
                {
                    try
                    {
                        var fileUrl = await _blobService.UploadImageAsync(file);

                        var media = new Media
                        {
                            FileUrl = fileUrl,
                            UploadedById = uploadedById,
                            Category = request.Category,
                            OwnerType = request.OwnerType,
                            OwnerId = uploadedById,
                        };

                        await _mediaRepository.AddAsync(media, cancellationToken);
                        uploadedMediaList.Add(media);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Blob storage upload error: {ErrorMessage}", ex.Message);
                        return OperationResult<List<MediaDto>>.Failure($"Upload failed: {ex.Message}");
                    }
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            foreach (var media in uploadedMediaList)
            {
                results.Add(
                    new MediaDto
                    {
                        Id = media.Id,
                        FileUrl = media.FileUrl,
                        Category = media.Category.ToString(),
                        OwnerId = media.OwnerId,
                        OwnerType = media.OwnerType.ToString(),
                    }
                );
            }

            _logger.LogInformation(
                "Successfully uploaded {Count} media files to blob storage for user {UserId}",
                results.Count,
                uploadedById
            );

            return OperationResult<List<MediaDto>>.Success(results, "Upload successful");
        }

        public async Task<OperationResult<MediaDto>> GetMediaByIdAsync(
            Guid mediaId,
            Guid requesterId,
            bool requesterIsAdmin,
            CancellationToken cancellationToken = default
        )
        {
            var media = await _mediaRepository.GetByIdAsync(mediaId, cancellationToken);
            if (media == null)
            {
                return OperationResult<MediaDto>.Failure("Media not found");
            }

            // Ảnh giấy tờ tuỳ thân (CCCD) chỉ chính chủ hoặc ADMIN được xem.
            if (
                media.Category == MediaCategory.Identification
                && media.UploadedById != requesterId
                && !requesterIsAdmin
            )
            {
                return OperationResult<MediaDto>.Failure("Media not found");
            }

            var dto = new MediaDto
            {
                Id = media.Id,
                FileUrl = media.FileUrl,
                Category = media.Category.ToString(),
                OwnerId = media.OwnerId,
                OwnerType = media.OwnerType.ToString()
            };

            return OperationResult<MediaDto>.Success(dto, "Get media successfully");
        }

        public async Task<OperationResult<List<MediaDto>>> GetMediaByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken = default
        )
        {
            var medias = await _mediaRepository.FindAsync(m => m.OwnerId == ownerId, cancellationToken);

            var results = medias.Select(media => new MediaDto
            {
                Id = media.Id,
                FileUrl = media.FileUrl,
                Category = media.Category.ToString(),
                OwnerId = media.OwnerId,
                OwnerType = media.OwnerType.ToString()
            }).ToList();

            return OperationResult<List<MediaDto>>.Success(results, "Get media by owner successfully");
        }

        public async Task<OperationResult<List<MediaDto>>> GetMyMediaAsync(
            MediaCategory? category = null,
            MediaOwnerType? ownerType = null,
            CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrWhiteSpace(_currentUserService.UserId) || !Guid.TryParse(_currentUserService.UserId, out var currentUserId))
            {
                return OperationResult<List<MediaDto>>.Failure("User not authenticated");
            }

            var medias = await _mediaRepository.FindAsync(m =>
                m.UploadedById == currentUserId &&
                (!category.HasValue || m.Category == category.Value) &&
                (!ownerType.HasValue || m.OwnerType == ownerType.Value),
                cancellationToken
            );

            var results = medias.Select(media => new MediaDto
            {
                Id = media.Id,
                FileUrl = media.FileUrl,
                Category = media.Category.ToString(),
                OwnerId = media.OwnerId,
                OwnerType = media.OwnerType.ToString()
            }).ToList();

            return OperationResult<List<MediaDto>>.Success(results, "Get my media successfully");
        }
    }
}
