using System.Text.RegularExpressions;
using Application.Interfaces.Services.Media;
using Application.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.Medias;

/// <summary>
/// Lưu ảnh vào thư mục cục bộ (volume Docker) thay cho Azure Blob.
/// File được phục vụ lại qua <c>GET /media/{tên file}</c> (MediaFileController),
/// nơi ảnh CCCD / ảnh khuôn mặt bị chặn nếu không đăng nhập đúng chủ hoặc ADMIN.
/// Giới hạn dung lượng và đuôi file giống hệt BlobService.
/// </summary>
public partial class LocalBlobService : IBlobService
{
    private const long MaxFileSize = 5 * 1024 * 1024;

    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly string _rootPath;
    private readonly string _publicBaseUrl;

    public LocalBlobService(IOptions<StorageSettings> options)
    {
        var settings = options.Value.Local;
        _rootPath = ResolveRoot(settings.RootPath);
        _publicBaseUrl = (settings.PublicBaseUrl ?? string.Empty).TrimEnd('/');
        Directory.CreateDirectory(_rootPath);
    }

    public static string ResolveRoot(string? rootPath) =>
        Path.GetFullPath(
            string.IsNullOrWhiteSpace(rootPath) ? "data/uploads" : rootPath,
            AppContext.BaseDirectory
        );

    /// <summary>Tên file hợp lệ: GUID + đuôi ảnh. Chặn mọi kiểu "../" hoặc tên lạ.</summary>
    public static bool IsValidFileName(string? fileName) =>
        !string.IsNullOrEmpty(fileName) && FileNameRegex().IsMatch(fileName);

    public async Task<string> UploadImageAsync(IFormFile file, string? oldImageUrl = null)
    {
        ValidateFile(file);

        if (!string.IsNullOrWhiteSpace(oldImageUrl))
        {
            await DeleteImageAsync(oldImageUrl);
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var fileName = $"{Guid.NewGuid()}{extension}";
        var fullPath = Path.Combine(_rootPath, fileName);

        await using (var target = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write))
        await using (var source = file.OpenReadStream())
        {
            await source.CopyToAsync(target);
        }

        return $"{_publicBaseUrl}/media/{fileName}";
    }

    public Task DeleteImageAsync(string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return Task.CompletedTask;

        try
        {
            var path = Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri)
                ? uri.LocalPath
                : imageUrl;
            var fileName = Path.GetFileName(path);

            if (!IsValidFileName(fileName))
                return Task.CompletedTask;

            var fullPath = Path.Combine(_rootPath, fileName);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
        catch { }

        return Task.CompletedTask;
    }

    private static void ValidateFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            throw new Exception("File is empty");
        }

        if (file.Length > MaxFileSize)
        {
            throw new Exception("File size exceeded maximum limit of 5MB");
        }

        var extension = Path.GetExtension(file.FileName).ToLower();

        if (!AllowedExtensions.Contains(extension))
        {
            throw new Exception("Only jpg, jpeg, png, webp files are allowed");
        }
    }

    [GeneratedRegex(
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\\.(jpg|jpeg|png|webp)$"
    )]
    private static partial Regex FileNameRegex();
}
