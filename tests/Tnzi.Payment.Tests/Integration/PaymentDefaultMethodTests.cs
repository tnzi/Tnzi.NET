using Microsoft.Extensions.DependencyInjection;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Providers;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 调用方没指定支付方式时，按渠道自己支持的方式建单，而不是一律按信用卡建、再被渠道判成「不支持」。
/// </summary>
/// <remarks>
/// 此前缺省值硬编码为 <c>CreditCard</c>：出厂默认渠道（线下）只收银行转账 / 线下汇款，PayPal 只收 PayPal，
/// 不带支付方式的建单在这两个渠道上一律 400 <c>PAYMENT_CHANNEL_NOT_SUPPORTED</c>，错误码还暗示渠道没启用。
/// 订阅模块的首期单与补差单从不传支付方式，于是线下与 PayPal 渠道上的订阅一条都开不出来。
/// </remarks>
public class PaymentDefaultMethodTests : PaymentIntegrationTestBase
{
    private readonly ScriptedChannelProvider _walletOnly = new() { Supports = m => m == PaymentMethod.PayPal };

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<PaymentOptions>(o =>
            o.Channels[ScriptedChannelProvider.Code] = new ChannelOptions { Enabled = true, Currency = "USD" });
        services.AddScoped<IPaymentProvider>(_ => _walletOnly);
    }

    private Task<Result<PaymentOrderResultDto>> CreateAsync(string channelCode, PaymentMethod? method = null) =>
        InScopeAsync<IPaymentService, Result<PaymentOrderResultDto>>(svc => svc.CreatePaymentAsync(new CreatePaymentDto
        {
            BusinessOrderNo = "ORDER-" + Guid.NewGuid().ToString("N")[..8],
            BusinessType = BusinessType.Order,
            Amount = 40m,
            Currency = "USD",
            ChannelCode = channelCode,
            PaymentMethod = method
        }));

    private async Task<PaymentEntity> LoadAsync(string tradeNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<Tnzi.Domain.Repositories.IRepository<PaymentEntity, Guid>>();
        return (await repo.FirstOrDefaultAsync(p => p.TradeNo == tradeNo))!;
    }

    [Fact]
    public async Task AnOfflinePaymentWithoutAMethod_IsCreatedWithAnOfflineMethod()
    {
        var created = await CreateAsync(PaymentConstants.OfflineChannelCode);

        created.Succeeded.ShouldBeTrue(created.Message);
        var payment = await LoadAsync(created.Data!.TradeNo);
        payment.Status.ShouldBe(PaymentStatus.Pending);
        payment.PaymentMethod.ShouldBeOneOf(PaymentMethod.BankTransfer, PaymentMethod.Offline);
    }

    [Fact]
    public async Task AChannelThatTakesOnlyItsOwnWallet_IsCreatedWithThatWallet()
    {
        var created = await CreateAsync(ScriptedChannelProvider.Code);

        created.Succeeded.ShouldBeTrue(created.Message);
        (await LoadAsync(created.Data!.TradeNo)).PaymentMethod.ShouldBe(PaymentMethod.PayPal);
    }

    /// <summary>能收卡的渠道照旧按信用卡记：缺省值只在渠道不收卡时才换。</summary>
    [Fact]
    public async Task ACardChannelWithoutAMethod_StillDefaultsToCreditCard()
    {
        var created = await CreateAsync(PaymentConstants.NullChannelCode);

        created.Succeeded.ShouldBeTrue(created.Message);
        (await LoadAsync(created.Data!.TradeNo)).PaymentMethod.ShouldBe(PaymentMethod.CreditCard);
    }

    /// <summary>调用方明说了一种渠道不收的方式：照旧拒绝，不替他换。</summary>
    [Fact]
    public async Task AnExplicitMethodTheChannelDoesNotTake_IsStillRefused()
    {
        var created = await CreateAsync(PaymentConstants.OfflineChannelCode, PaymentMethod.CreditCard);

        created.Succeeded.ShouldBeFalse();
        created.Message.ShouldBe(ErrorCodes.PaymentChannelNotSupported);
    }
}
