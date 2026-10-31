using System.Security.Claims;
using Application.DTOs.WorkerProfile;
using Application.DTOs.WorkerProfile.WorkerCertificate;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using API.Extensions;

namespace API.Controllers
{
    [Route("api/worker-profiles")]
    [ApiController]
    public class WorkerProfileController : ApiController
    {
        private readonly IWorkerProfileService _workerProfileService;
        private readonly IFaceRecognitionService _faceRecognitionService;

        public WorkerProfileController(
            IWorkerProfileService workerProfileService,
            IFaceRecognitionService faceRecognitionService
        )
        {
            _workerProfileService = workerProfileService;
            _faceRecognitionService = faceRecognitionService;
        }

        [Authorize(Roles = "WORKER")]
        [HttpPost("register")]
        public async Task<IActionResult> Register(
            [FromForm] WorkerRegisterRequestDto dto,
            CancellationToken cancellationToken
        )
        {
            var result = await _workerProfileService.WorkerRegisterAsync(dto, cancellationToken);

            return HandleResult(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetPaged(
            [FromQuery] WorkerProfileQuery query,
            CancellationToken cancellationToken
        )
        {
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var result = await _workerProfileService.GetPagedWorkerProfiles(
                query,
                role,
                cancellationToken
            );

            return HandleResult(result);
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchWorkers(
            [FromQuery] CustomerWorkerSearchQuery query,
            CancellationToken cancellationToken
        )
        {
            var result = await _workerProfileService.SearchWorkersForCustomerAsync(
                query,
                cancellationToken
            );

            return HandleResult(result);
        }

        [HttpGet("{workerId:guid}/public")]
        public async Task<IActionResult> GetPublicDetail(
            Guid workerId,
            CancellationToken cancellationToken
        )
        {
            var result = await _workerProfileService.GetPublicDetailAsync(
                workerId,
                cancellationToken
            );

            return HandleResult(result);
        }

        [Authorize]
        [HttpGet("{workerId:guid}/private")]
        public async Task<IActionResult> GetPrivateDetail(
            Guid workerId,
            CancellationToken cancellationToken
        )
        {
            var result = await _workerProfileService.GetPrivateDetailAsync(
                workerId,
                cancellationToken
            );

            return HandleResult(result);
        }

        [Authorize(Roles = "ADMIN")]
        [HttpGet("{workerId:guid}/admin")]
        public async Task<IActionResult> GetAdminDetail(
            Guid workerId,
            CancellationToken cancellationToken
        )
        {
            var result = await _workerProfileService.GetAdminAndOwnerDetailAsync(
                workerId,
                cancellationToken
            );

            return HandleResult(result);
        }

        [Authorize(Roles = "ADMIN")]
        [HttpPut("{id:guid}/approve")]
        public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
        {
            var adminId = GetUserId();

            var result = await _workerProfileService.ApproveWorkerRegisterRequest(
                id,
                adminId,
                cancellationToken
            );

            return HandleResult(result);
        }

        [Authorize(Roles = "ADMIN")]
        [HttpPut("{id:guid}/reject")]
        public async Task<IActionResult> Reject(
            Guid id,
            [FromBody] RejectWorkerProfileRequestDto dto,
            CancellationToken cancellationToken
        )
        {
            var result = await _workerProfileService.RejectWorkerRegisterRequest(
                id,
                dto.Reason,
                cancellationToken
            );

            return HandleResult(result);
        }

        [Authorize(Roles = "WORKER")]
        [HttpGet("me")]
        public async Task<IActionResult> GetMyProfile(CancellationToken cancellationToken)
        {
            var workerId = GetUserId();

            var result = await _workerProfileService.GetAdminAndOwnerDetailAsync(
                workerId,
                cancellationToken
            );

            return HandleResult(result);
        }

        [Authorize(Roles = "WORKER")]
        [HttpPatch("me")]
        public async Task<IActionResult> UpdateProfile(
            [FromForm] WorkerProfileUpdateRequestDto dto,
            CancellationToken cancellationToken
        )
        {
            var workerId = GetUserId();

            var result = await _workerProfileService.UpdateWorkerProfileAsync(
                workerId,
                dto,
                cancellationToken
            );

            return HandleResult(result);
        }

        [Authorize(Roles = "WORKER")]
        [HttpPatch("me/working-status")]
        public async Task<IActionResult> UpdateWorkingStatus(
            [FromBody] UpdateWorkingStatusRequestDto dto,
            CancellationToken cancellationToken
        )
        {
            var workerId = GetUserId();

            var result = await _workerProfileService.UpdateWorkingStatusAsync(
                workerId,
                dto,
                cancellationToken
            );

            return HandleResult(result);
        }

        [Authorize(Roles = "WORKER")]
        [HttpGet("me/deposit-status")]
        public async Task<IActionResult> GetDepositStatus(CancellationToken cancellationToken)
        {
            var workerId = GetUserId();

            var result = await _workerProfileService.GetDepositStatusAsync(
                workerId,
                cancellationToken
            );

            return HandleResult(result);
        }

        [Authorize(Roles = "WORKER")]
        [HttpPost("me/request-offboarding")]
        public async Task<IActionResult> RequestOffboarding(
            [FromBody] RequestOffboardingDto dto,
            CancellationToken cancellationToken
        )
        {
            var workerId = GetUserId();

            var result = await _workerProfileService.RequestOffboardingAsync(
                workerId,
                dto,
                cancellationToken
            );

            return HandleResult(result);
        }

        [Authorize(Roles = "WORKER")]
        [HttpPost("me/portfolio-images")]
        public async Task<IActionResult> UploadPortfolioImages(
            [FromForm] UploadPortfolioImagesRequestDto dto,
            CancellationToken cancellationToken
        )
        {
            var workerId = GetUserId();

            var result = await _workerProfileService.UploadPortfolioImagesAsync(
                workerId,
                dto,
                cancellationToken
            );

            return HandleResult(result);
        }

        [Authorize(Roles = "WORKER")]
        [HttpDelete("me/portfolio-images/{mediaId:guid}")]
        public async Task<IActionResult> DeletePortfolioImage(
            Guid mediaId,
            CancellationToken cancellationToken
        )
        {
            var workerId = GetUserId();

            var result = await _workerProfileService.DeletePortfolioImageAsync(
                workerId,
                mediaId,
                cancellationToken
            );

            return HandleResult(result);
        }

        [Authorize(Roles = "WORKER")]
        [HttpPut("me/identification-images")]
        public async Task<IActionResult> UpdateIdentificationImages(
            [FromForm] UpdateIdentificationRequestDto dto,
            CancellationToken cancellationToken
        )
        {
            var workerId = GetUserId();

            var result = await _workerProfileService.UpdateIdentificationAsync(
                workerId,
                dto,
                cancellationToken
            );

            return HandleResult(result);
        }

        [Authorize(Roles = "WORKER")]
        [HttpPut("me/centificates")]
        public async Task<IActionResult> UpdateIdentificationImages(
            [FromForm] List<WorkerCertificateUploadRequestDto> dtos,
            CancellationToken cancellationToken
        )
        {
            var workerId = GetUserId();

            var result = await _workerProfileService.UpdateCentificatesAsync(
                workerId,
                dtos,
                cancellationToken
            );

            return HandleResult(result);
        }

        // Tốn CPU (mô hình ONNX) — bắt buộc đăng nhập và giới hạn tần suất.
        [Authorize]
        [EnableRateLimiting(RateLimiterExtensions.OtpSendPolicy)]
        [HttpPost("verify-face")]
        public async Task<IActionResult> VerifyFace(
            [FromForm] VerifyFaceRequestDto dto,
            CancellationToken cancellationToken
        )
        {
            if (dto.CardFrontImage == null || dto.CardFrontImage.Length == 0)
            {
                return BadRequest(Application.Common.OperationResult<FaceMatchResultDto>.Failure("Ảnh mặt trước CCCD không hợp lệ."));
            }

            if (dto.SelfieImage == null || dto.SelfieImage.Length == 0)
            {
                return BadRequest(Application.Common.OperationResult<FaceMatchResultDto>.Failure("Ảnh chân dung (Selfie) không hợp lệ."));
            }

            using var cardStream = dto.CardFrontImage.OpenReadStream();
            using var selfieStream = dto.SelfieImage.OpenReadStream();

            var result = await _faceRecognitionService.CompareFacesAsync(
                cardStream,
                selfieStream,
                cancellationToken
            );

            return Ok(Application.Common.OperationResult<FaceMatchResultDto>.Success(result));
        }
    }
}
