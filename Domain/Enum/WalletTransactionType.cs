namespace Domain.Enum
{
    public enum WalletTransactionType
    {
        TopUp, // nạp tiền
        Payment, // khách trả tiền đơn hàng
        Refund, // hoàn tiền
        Commission, // hoa hồng hệ thống lấy
        BookingIncome, // cộng tiền vào ví thợ
        Withdrawal, // thợ rút tiền
        Fee, // phí nền tảng (service fee)
        Adjustment, // admin chỉnh tay
        DepositLock, // nạp tiền cọc ký quỹ kích hoạt nhận việc
        DepositRefund, // hoàn trả 100% tiền cọc khi thôi việc
        PlatformFeeHold, // giữ phí nền tảng (phí + VAT) khi KTV nhận đơn tiền mặt
        PlatformFeeRelease, // hoàn khoản giữ phí nền tảng khi đơn tiền mặt bị huỷ
        CancellationCompensation, // KTV nhận phần phí huỷ bù chi phí đi lại
    }
}
