using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Providers;
using Tnzi.Payment.Services;
using Tnzi.Results;
using Tnzi.TestBase;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 订阅收款只向渠道要「能留存、之后能无人值守再扣」的支付方式；普通订单不受限。
/// </summary>
/// <remarks>
/// 消费方实测：开通与补缴的 Payment Element 列出了 Card 与 Klarna。先买后付付了首期，第一次续费就无卡可扣。
/// 首付时账户已经有卡（这一次不存卡）也一样要限：之后的每一期都靠卡。
/// </remarks>
public class SubscriptionPaymentMethodRestrictionTests : PaymentIntegrationTestBase
{
    private readonly List<PaymentProviderCreateDto> _created = [];

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<PaymentOptions>(o => o.Channels["Recorder"] = new ChannelOptions { Enabled = true, Currency = "CAD" });

        var provider = new Mock<IPaymentProvider>();
        provider.SetupGet(p => p.ChannelCode).Returns("Recorder");
        provider.SetupGet(p => p.SupportsPaymentMethodStorage).Returns(true);
        provider.Setup(p => p.IsSupported(It.IsAny<PaymentMethod>())).Returns(true);
        provider
            .Setup(p => p.CreatePaymentAsync(It.IsAny<PaymentProviderCreateDto>()))
            .Callback((PaymentProviderCreateDto input) => _created.Add(input))
            .ReturnsAsync((PaymentProviderCreateDto input) => Result.Success(new PaymentProviderOrderResult { TradeNo = input.TradeNo, ExternalTradeNo = "pi_1", PayParams = "pi_1_secret" }));
        services.AddScoped(_ => provider.Object);
    }

    private Task<Result<PaymentOrderResultDto>> CreateAsync(BusinessType type, bool savePaymentMethod) =>
        InScopeAsync<IPaymentService, Result<PaymentOrderResultDto>>(svc => svc.CreatePaymentAsync(new CreatePaymentDto
        {
            BusinessOrderNo = $"B{Guid.NewGuid():N}"[..12],
            BusinessType = type,
            Amount = 100m,
            Currency = "CAD",
            ChannelCode = "Recorder",
            IsSystemInitiated = true,
            BillingAccount = BillingAccountRef.ForUser(TestHelper.DefaultTestUserId),
            SavePaymentMethod = savePaymentMethod
        }));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ASubscriptionPayment_AsksTheChannelForReusableMethodsOnly(bool savingTheCard)
    {
        var created = await CreateAsync(BusinessType.Subscription, savingTheCard);

        created.Succeeded.ShouldBeTrue(created.Message);
        _created.ShouldHaveSingleItem().ReusableMethodsOnly.ShouldBeTrue();
    }

    [Fact]
    public async Task AnOrdinaryOrder_IsNotRestricted()
    {
        var created = await CreateAsync(BusinessType.Order, savePaymentMethod: false);

        created.Succeeded.ShouldBeTrue(created.Message);
        _created.ShouldHaveSingleItem().ReusableMethodsOnly.ShouldBeFalse();
    }
}
