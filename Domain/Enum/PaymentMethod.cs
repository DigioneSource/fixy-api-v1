using System.ComponentModel;

namespace Domain.Enum
{
    public enum PaymentMethod
    {
        [Description("Ví Fixy")]
        Wallet = 0,
        [Description("VNPay")]
        Vnpay = 1,
        [Description("MoMo")]
        Momo = 2,
        [Description("PayOS")]
        PayOS = 3,
        [Description("Thẻ ngân hàng")]
        Card = 4,
        [Description("Tiền mặt")]
        Cash = 5,
        [Description("Chuyển khoản QR")]
        SePay = 6,
    }
}
