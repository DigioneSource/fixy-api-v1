using Application.DTOs.Media;
using Domain.Enum;

namespace Application.DTOs.Booking
{
    public class BookingDetailDto
    {
        public Guid Id { get; set; }
        public Guid CustomerProfileId { get; set; }
        public Guid? WorkerProfileId { get; set; }
        public string? WorkerName { get; set; }
        public string? WorkerPhone { get; set; }
        public string? WorkerAvatarUrl { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerPhone { get; set; }
        public string? CustomerAvatarUrl { get; set; }
        public Guid CategoryId { get; set; }
        public string Address { get; set; } = string.Empty;
        public double Lat { get; set; }
        public double Lng { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ScheduledType { get; set; } = string.Empty;
        public DateTime? ScheduledAt { get; set; }
        public int? TotalDurationMinutes { get; set; }
        public PaymentMethod? PaymentMethod { get; set; }
        public string? PaymentMethodName { get; set; }
        public long? EstimatedPrice { get; set; }
        public long? FinalPrice { get; set; }

        /// <summary>Phí nền tảng chưa thuế. Chỉ trả cho KTV của đơn và ADMIN.</summary>
        public long? PlatformFee { get; set; }

        /// <summary>VAT trên phí nền tảng. Chỉ trả cho KTV của đơn và ADMIN.</summary>
        public long? PlatformFeeVat { get; set; }

        /// <summary>KTV thực nhận. Chỉ trả cho KTV của đơn và ADMIN.</summary>
        public long? WorkerNet { get; set; }

        /// <summary>Phí huỷ (nếu có).</summary>
        public long? CancellationFee { get; set; }
        public string? CancelReason { get; set; }
        public DateTime? CancelledAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public List<MediaDto> RequestImages { get; set; } = new List<MediaDto>();

        public List<MediaDto> CompleteImages { get; set; } = new List<MediaDto>();
    }
}
