using System.Security.Claims;
using Application.Settings;
using Domain.Enum;
using Infrastructure.Persistence;
using Infrastructure.Services.Medias;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace API.Controllers
{
    /// <summary>
    /// Phục vụ ảnh khi Storage:Provider = Local.
    /// - Ảnh thường (avatar, portfolio, ảnh hoàn thành đơn, review...): công khai, tên file là GUID khó đoán.
    /// - Ảnh nhạy cảm (Media.Category = Identification tức ảnh CCCD, và User.FaceImageUrl tức ảnh khuôn mặt):
    ///   chỉ trả cho chính chủ hoặc ADMIN, có JWT qua header Authorization hoặc ?access_token=.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("media")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class MediaFileController : ControllerBase
    {
        private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp",
        };

        private readonly AppDbContext _context;
        private readonly StorageSettings _settings;

        public MediaFileController(AppDbContext context, IOptions<StorageSettings> options)
        {
            _context = context;
            _settings = options.Value;
        }

        [HttpGet("{fileName}")]
        public async Task<IActionResult> Get(string fileName, CancellationToken cancellationToken)
        {
            if (!string.Equals(_settings.Provider, StorageSettings.ProviderLocal, StringComparison.OrdinalIgnoreCase))
                return NotFound();

            if (!LocalBlobService.IsValidFileName(fileName))
                return NotFound();

            var root = LocalBlobService.ResolveRoot(_settings.Local.RootPath);
            var fullPath = Path.Combine(root, fileName);
            if (!System.IO.File.Exists(fullPath))
                return NotFound();

            var suffix = "/media/" + fileName;

            // Ảnh CCCD: chủ sở hữu là OwnerId / UploadedById của bản ghi Media.
            var identificationOwners = await _context.Media
                .IgnoreQueryFilters()
                .Where(m => m.Category == MediaCategory.Identification && m.FileUrl.EndsWith(suffix))
                .Select(m => new { m.OwnerId, m.UploadedById })
                .ToListAsync(cancellationToken);

            // Ảnh khuôn mặt eKYC lưu ở User.FaceImageUrl.
            var faceOwners = await _context.Users
                .IgnoreQueryFilters()
                .Where(u => u.FaceImageUrl != null && u.FaceImageUrl.EndsWith(suffix))
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            var isSensitive = identificationOwners.Count > 0 || faceOwners.Count > 0;

            if (isSensitive)
            {
                if (User.Identity?.IsAuthenticated != true)
                    return Unauthorized();

                var isAdmin = User.IsInRole("ADMIN");
                var userIdRaw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var isOwner = Guid.TryParse(userIdRaw, out var userId)
                    && (identificationOwners.Any(o => o.OwnerId == userId || o.UploadedById == userId)
                        || faceOwners.Contains(userId));

                if (!isAdmin && !isOwner)
                    return Forbid();

                Response.Headers.CacheControl = "private, no-store";
            }
            else
            {
                Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            }

            Response.Headers["X-Content-Type-Options"] = "nosniff";
            var contentType = ContentTypes[Path.GetExtension(fileName)];
            return PhysicalFile(fullPath, contentType);
        }
    }
}
