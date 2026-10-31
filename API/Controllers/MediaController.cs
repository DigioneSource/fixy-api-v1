using Application.DTOs.Media;
using Application.Interfaces.Services.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    [Authorize]
    public class MediaController : ApiController
    {
        private readonly IMediaService _mediaService;

        public MediaController(IMediaService mediaService)
        {
            _mediaService = mediaService;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> Upload([FromForm] UploadMediaFormDto request,CancellationToken cancellationToken)
        {
            var result = await _mediaService.UploadMediaAsync(request, cancellationToken);
            return HandleResult(result);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        {
            var result = await _mediaService.GetMediaByIdAsync(
                id,
                GetUserId(),
                User.IsInRole("ADMIN"),
                cancellationToken
            );
            return HandleResult(result);
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetMyMedia(
            [FromQuery] Domain.Enum.MediaCategory? category,
            [FromQuery] Domain.Enum.MediaOwnerType? ownerType,
            CancellationToken cancellationToken
        )
        {
            var result = await _mediaService.GetMyMediaAsync(category, ownerType, cancellationToken);
            return HandleResult(result);
        }
    }
}
