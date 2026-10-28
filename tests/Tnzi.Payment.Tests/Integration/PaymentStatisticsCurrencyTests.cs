using Microsoft.Extensions.DependencyInjection;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 统计总览带上金额的币种：管理端此前把加元总额印成裸「$」。混了币种时如实说不出一个币种。
/// </summary>
public class PaymentStatisticsCurrencyTests : PaymentIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddScoped<IPaymentStatisticsService, PaymentStatisticsService>();
        services.Configure<PaymentOptions>(o => o.DefaultCurrency = "CAD");
    }

    private Task SeedPaidAsync(string currency, decimal amount) => SeedAsync(new PaymentEntity
    {
        TradeNo = $"PAY{Guid.NewGuid():N}"[..20],
        BusinessOrderNo = "ORDER-1",
        BusinessType = BusinessType.Order,
        OriginalAmount = amount,
        PayableAmount = amount,
        PaidAmount = amount,
        Currency = currency,
        Status = PaymentStatus.Succeeded,
        ChannelCode = "Null",
        PaymentMethod = PaymentMethod.CreditCard,
        PaidTime = DateTime.UtcNow
    });

    private Task<Result<PaymentStatisticsDto>> OverviewAsync() =>
        InScopeAsync<IPaymentStatisticsService, Result<PaymentStatisticsDto>>(svc => svc.GetStatisticsAsync(new StatisticsQueryDto
        {
            StartTime = DateTime.UtcNow.AddDays(-1),
            EndTime = DateTime.UtcNow.AddMinutes(5)
        }));

    [Fact]
    public async Task OneCurrencyCollected_IsTheCurrencyOfTheTotals()
    {
        await SeedPaidAsync("CAD", 113m);
        await SeedPaidAsync("CAD", 226m);

        (await OverviewAsync()).Data!.Currency.ShouldBe("CAD");
    }

    [Fact]
    public async Task NothingCollected_UsesTheDeploymentDefault()
    {
        (await OverviewAsync()).Data!.Currency.ShouldBe("CAD");
    }

    [Fact]
    public async Task MixedCurrencies_HaveNoSingleCurrency()
    {
        await SeedPaidAsync("CAD", 113m);
        await SeedPaidAsync("USD", 50m);

        (await OverviewAsync()).Data!.Currency.ShouldBeNull("the total is a plain sum of two currencies");
    }
}
