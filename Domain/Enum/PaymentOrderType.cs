namespace Domain.Enum
{
    public enum PaymentOrderType
    {
        WalletTopUp,
        BookingPayment,
        WorkerDeposit, // Nạp tiền cọc ký quỹ kích hoạt nhận dịch vụ Spa
        WorkerWalletTopUp, // KTV nạp số dư bảo đảm cho đơn tiền mặt (CEO 31/10)
    }
}
