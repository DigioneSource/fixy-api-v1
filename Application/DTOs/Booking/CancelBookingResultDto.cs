namespace Application.DTOs.Booking
{
    /// <summary>Kết quả POST /api/bookings/{id}/cancel (CEO 31/10: phí huỷ 30% từ khi KTV xuất phát).</summary>
    public class CancelBookingResultDto
    {
        public Guid BookingId { get; set; }

        /// <summary>Phí huỷ. 0 nếu huỷ trước khi KTV xuất phát.</summary>
        public long CancellationFee { get; set; }

        /// <summary>Số tiền hoàn vào ví khách (đơn trả trước).</summary>
        public long RefundAmount { get; set; }

        /// <summary>Phần phí huỷ KTV nhận (70% phí huỷ).</summary>
        public long WorkerCompensation { get; set; }

        /// <summary>
        /// false khi đơn tiền mặt có phí huỷ: chưa thu được tiền khách, phí chỉ được ghi nhận là phải thu.
        /// </summary>
        public bool CancellationFeeCollected { get; set; }
    }
}
