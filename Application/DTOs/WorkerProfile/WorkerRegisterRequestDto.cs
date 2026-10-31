using Application.DTOs.Address;
using Application.DTOs.WorkerProfile.WorkerCertificate;
using Application.DTOs.WorkerProfile.WorkerService;
using Microsoft.AspNetCore.Http;

namespace Application.DTOs.WorkerProfile
{
    public class WorkerRegisterRequestDto
    {
        public string? Target { get; set; }
        public string? Phone { get; set; }
        public string? Bio { get; set; }
        public int ExperienceYears { get; set; }
        public int MaxDistanceKm { get; set; }

        /// <summary>Phiên bản "Hợp đồng sử dụng dịch vụ nền tảng" KTV đã đồng ý (tuỳ chọn).</summary>
        public string? PartnerTermsVersion { get; set; }

        // Identification
        public string CitizenIdNumber { get; set; } = default!;

        public DateTime CitizenIdIssueDate { get; set; }

        public string CitizenIdIssuePlace { get; set; } = default!;
        public CreateAddressRequestDto CreateAddressRequestDto { get; set; } =
            new CreateAddressRequestDto();
        public List<IFormFile> IdentificationUploads { get; set; } = new List<IFormFile>();
        public IFormFile? FaceSelfieUpload { get; set; }
        public List<IFormFile> PortfolioUploads { get; set; } = new List<IFormFile>();

        public List<WorkerServiceRegisterRequestDto> WorkerService { get; set; } =
            new List<WorkerServiceRegisterRequestDto>();
        public List<WorkerCertificateUploadRequestDto> CertificateUploads { get; set; } =
            new List<WorkerCertificateUploadRequestDto>();
    }
}
