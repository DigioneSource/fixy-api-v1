using Domain.Common;
using Domain.Enum;

namespace Domain.Entity
{
    public class WorkerProfile : BaseEntity
    {
        public Guid UserId { get; set; }

        // =========================
        // Profile
        // =========================

        public string? Bio { get; set; }

        public string? BioEn { get; set; }

        public string? BioKo { get; set; }

        public int ExperienceYears { get; set; }

        public DateTime? ProfileUpdatedAt { get; set; }

        // =========================
        // Working Area
        // =========================

        public int MaxDistanceKm { get; set; }

        // =========================
        // Realtime Status
        // =========================

        public bool IsOnline { get; set; }

        public bool IsBusy { get; set; }

        public bool IsAcceptingJobs { get; set; }

        // =========================
        // Realtime Location
        // =========================

        public double? CurrentLat { get; set; }

        public double? CurrentLng { get; set; }

        public DateTime? LastLocationAt { get; set; }

        // =========================
        // Business
        // =========================

        public WorkerStatus Status { get; set; } = WorkerStatus.Pending;

        public string? RejectReason { get; set; }

        // =========================
        // Statistics
        // =========================

        public WorkerBadge Badge { get; set; } = WorkerBadge.NewArrival;

        public double RatingAvg { get; set; }

        public int TotalReviews { get; set; }

        public int TotalOrders { get; set; }

        // =========================
        // Feature
        // =========================

        // =========================
        // Partner terms (Hợp đồng sử dụng dịch vụ nền tảng — bằng chứng uỷ quyền thu hộ, uỷ nhiệm hoá đơn)
        // =========================

        /// <summary>Phiên bản hợp đồng KTV đã đồng ý khi đăng ký (VD "2026-10-31.2").</summary>
        public string? PartnerTermsVersion { get; set; }

        /// <summary>Thời điểm (UTC) KTV đồng ý hợp đồng.</summary>
        public DateTime? PartnerTermsAcceptedAt { get; set; }

        public DateTime? FeaturedUntil { get; set; }

        public FeaturedPackage? FeaturedPackage { get; set; }

        // =========================
        // Security Deposit (Tiền cọc ký quỹ)
        // =========================

        public bool IsDepositPaid { get; set; } = false;

        public long DepositRequiredAmount { get; set; } // Bằng BasePrice dịch vụ Spa chính (IsPrimary)

        public DateTime? DepositPaidAt { get; set; }

        public bool IsOffboardingRequested { get; set; } = false;

        public DateTime? OffboardingRequestedAt { get; set; }

        // =========================
        // Approval
        // =========================

        public DateTime? ApprovedAt { get; set; }

        public Guid? ApprovedById { get; set; }

        // =========================
        // Navigation
        // =========================

        public User? User { get; set; }

        public User? ApprovedBy { get; set; }

        // =========================
        // Worker Domain
        // =========================

        // Worker chỉ có 1 address
        public Address? Address { get; set; }

        public ICollection<WorkerCertificate> Certificates { get; set; } =
            new List<WorkerCertificate>();

        public ICollection<WorkerService> Services { get; set; } = new List<WorkerService>();

        public ICollection<Booking> Bookings { get; set; } = new List<Booking>();

        public ICollection<WorkerWeeklySchedule> WeeklySchedules { get; set; } =
            new List<WorkerWeeklySchedule>();

        public ICollection<WorkerScheduleException> ScheduleExceptions { get; set; } =
            new List<WorkerScheduleException>();

        public ICollection<WorkerPayoutAccount> PayoutAccounts { get; set; } =
            new List<WorkerPayoutAccount>();

        public ICollection<WorkerFeaturedOrder> FeaturedOrders { get; set; } =
            new List<WorkerFeaturedOrder>();

        public ICollection<WorkerEarning> Earnings { get; set; } = new List<WorkerEarning>();

        public ICollection<Review> Reviews { get; set; } = new List<Review>();
    }
}
