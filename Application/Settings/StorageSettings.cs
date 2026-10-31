namespace Application.Settings
{
    /// <summary>
    /// Chọn nơi lưu ảnh tải lên. Cấu hình qua section "Storage".
    /// Provider = "AzureBlob" (mặc định, giữ hành vi cũ) | "Local" (lưu thư mục trên máy chủ).
    /// </summary>
    public class StorageSettings
    {
        public const string ProviderAzureBlob = "AzureBlob";
        public const string ProviderLocal = "Local";
        public const string ProviderCloudinary = "Cloudinary";

        public string Provider { get; set; } = ProviderAzureBlob;

        public LocalStorageSettings Local { get; set; } = new();
    }

    public class LocalStorageSettings
    {
        /// <summary>Thư mục lưu file trong container, ví dụ /app/data/uploads (gắn volume).</summary>
        public string RootPath { get; set; } = "data/uploads";

        /// <summary>
        /// Địa chỉ công khai của API, ví dụ https://api.tayzen.vn.
        /// URL ảnh trả về = PublicBaseUrl + "/media/" + tên file.
        /// </summary>
        public string PublicBaseUrl { get; set; } = string.Empty;
    }
}
