using Application.DTOs.Address;
using Application.DTOs.Media;
using Application.DTOs.WorkerProfile.WorkerCertificate;
using Application.DTOs.WorkerProfile.WorkerService;
using Domain.Enum;

public class WorkerAdminAndOwnerDetailDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    public string Email { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }

    public string? Gender { get; set; }

    public WorkerStatus Status { get; set; }

    public bool IsOnline { get; set; }
    public bool IsAcceptingJobs { get; set; }
    public bool IsBusy { get; set; }
    public bool IsOffboardingRequested { get; set; }
    public DateTime? OffboardingRequestedAt { get; set; }
    public string? PartnerTermsVersion { get; set; }
    public DateTime? PartnerTermsAcceptedAt { get; set; }

    public string? Bio { get; set; }

    public int ExperienceYears { get; set; }
    public double RatingAvg { get; set; }
    public int TotalReviews { get; set; }
    public int TotalOrders { get; set; }
    public double MaxDistanceKm { get; set; }

    public string? CitizenIdNumber { get; set; }

    public string? CitizenIdIssuePlace { get; set; }

    public DateTime? CitizenIdIssueDate { get; set; }

    public string? RejectReason { get; set; }
    public AddressDto? Address { get; set; } = new();

    public List<WorkerServiceDto> Services { get; set; } = [];

    public List<WorkerCertificateDto> Certificates { get; set; } = [];

    public List<MediaDto> IdentificationImages { get; set; } = [];

    public List<MediaDto> PortfolioImages { get; set; } = [];
}
