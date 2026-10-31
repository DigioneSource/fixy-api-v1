using Domain.Common;
using Domain.Enum;

namespace Domain.Entity
{
    public class Booking : BaseAuditableEntity, ISoftDelete
    {
        // =========================
        // Ownership
        // =========================

        public Guid CustomerProfileId { get; set; }

        public Guid? WorkerProfileId { get; set; }

        // =========================
        // Booking Info
        // =========================

        public Guid CategoryId { get; set; }

        public string Address { get; set; } = string.Empty;

        public double Lat { get; set; }

        public double Lng { get; set; }

        public BookingScheduledType ScheduledType { get; set; }

        public DateTime? ScheduledAt { get; set; }

        public int? TotalDurationMinutes { get; set; }

        public BookingStatus Status { get; set; } = BookingStatus.Pending;

        // =========================
        // Pricing
        // =========================

        public long? EstimatedPrice { get; set; }

        public long? FinalPrice { get; set; }

        // =========================
        // Platform fee (CEO 31/10/2026 — mô hình trung gian)
        // =========================

        /// <summary>Phí nền tảng chưa thuế = round(30% × giá dịch vụ trước voucher). Chốt khi đơn hoàn thành.</summary>
        public long? PlatformFee { get; set; }

        /// <summary>Thuế GTGT trên phí nền tảng = round(10% × PlatformFee).</summary>
        public long? PlatformFeeVat { get; set; }

        /// <summary>KTV thực nhận = giá dịch vụ − PlatformFee − PlatformFeeVat.</summary>
        public long? WorkerNet { get; set; }

        /// <summary>Đơn tiền mặt: số tiền (phí + VAT) đang tạm giữ trong ví KTV từ lúc nhận đơn.</summary>
        public long? HeldPlatformFee { get; set; }

        /// <summary>Phí huỷ (khách huỷ từ lúc KTV xuất phát). Đơn tiền mặt: chỉ là số phải thu.</summary>
        public long? CancellationFee { get; set; }

        // =========================
        // E-Contract Terms
        // =========================

        public bool AcceptedTerms { get; set; }

        public DateTime? TermsAcceptedAt { get; set; }

        // =========================
        // Cancellation
        // =========================

        public string? CancelReason { get; set; }

        public Guid? CancelledById { get; set; }

        public DateTime? CancelledAt { get; set; }

        // =========================
        // Completion
        // =========================

        public DateTime? CompletedAt { get; set; }

        // =========================
        // Reorder
        // =========================

        public Guid? ReorderFromId { get; set; }

        // =========================
        // Soft Delete
        // =========================

        public bool IsDeleted { get; set; }

        public DateTime? DeletedDate { get; set; }

        public string? DeletedBy { get; set; }

        // =========================
        // Navigation
        // =========================

        public CustomerProfile? CustomerProfile { get; set; }

        public WorkerProfile? WorkerProfile { get; set; }

        public ServiceCategory? Category { get; set; }

        public User? CancelledBy { get; set; }

        public Booking? ReorderFrom { get; set; }

        public ICollection<Booking> Reorders { get; set; } = new List<Booking>();

        public ICollection<WorkerMatchingQueue> MatchingQueue { get; set; } =
            new List<WorkerMatchingQueue>();

        public PaymentOrder? PaymentOrder { get; set; }

        public Invoice? Invoice { get; set; }

        public BookingVoucher? BookingVoucher { get; set; }

        public ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();

        public Review? Review { get; set; }

        public WorkerEarning? WorkerEarning { get; set; }

        public ICollection<SupportTicket> SupportTickets { get; set; } = new List<SupportTicket>();
    }
}
