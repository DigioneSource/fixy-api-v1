using Application.Interfaces.Services.Payment;
using Domain.Enum;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services.Payment
{
    public class PaymentGatewayFactory : IPaymentGatewayFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public PaymentGatewayFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IPaymentGateway Get(PaymentMethod method)
        {
            return method switch
            {
                PaymentMethod.Vnpay => _serviceProvider.GetRequiredService<VnPayService>(),
                PaymentMethod.Momo => _serviceProvider.GetRequiredService<MoMoService>(),
                PaymentMethod.PayOS => _serviceProvider.GetRequiredService<PayOSService>(),
                PaymentMethod.Card => _serviceProvider.GetRequiredService<PayOSService>(),
                PaymentMethod.SePay => _serviceProvider.GetRequiredService<SePayGatewayService>(),
                _ => _serviceProvider.GetRequiredService<PayOSService>(),
            };
        }
    }
}
