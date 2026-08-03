using Microsoft.Extensions.DependencyInjection;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 价外税集成测试：税额必须真正计入应付额并作为回调金额校验基准。
/// TaxOptions 此前只有配置没有消费者，改了不生效。
/// </summary>
public class ExclusiveTaxIntegrationTests : PaymentIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<TaxOptions>(o =>
        {
            o.Enabled = true;
            o.DefaultTaxRate = 13m;
            o.TaxIncluded = false;
        });
    }

    [Fact]
    public async Task CreatePayment_AddsTaxOnTopOfNetAmount()
    {
        var created = await InScopeAsync<IPaymentService, Result<PaymentOrderResultDto>>(
            svc => svc.CreatePaymentAsync(new CreatePaymentDto
            {
                BusinessOrderNo = "ORDER-TAX",
                BusinessType = BusinessType.Order,
                Amount = 100m,
                Currency = "USD",
                ChannelCode = "Null"
            }));

        created.Succeeded.ShouldBeTrue();
        created.Data!.TaxAmount.ShouldBe(13m);
        created.Data.Amount.ShouldBe(113m);

        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<Tnzi.Domain.Repositories.IRepository<PaymentEntity, Guid>>();
        var payment = await repo.FirstOrDefaultAsync(p => p.TradeNo == created.Data.TradeNo);

        payment!.PayableAmount.ShouldBe(113m);
        payment.TaxAmount.ShouldBe(13m);
        payment.OriginalAmount.ShouldBe(100m);
    }
}

/// <summary>
/// 价内税集成测试：标价即应付额，税额只做拆分列示，不额外加价。
/// </summary>
public class InclusiveTaxIntegrationTests : PaymentIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<TaxOptions>(o =>
        {
            o.Enabled = true;
            o.DefaultTaxRate = 25m;
            o.TaxIncluded = true;
        });
    }

    [Fact]
    public async Task CreatePayment_KeepsListedPriceAndSplitsOutTax()
    {
        var created = await InScopeAsync<IPaymentService, Result<PaymentOrderResultDto>>(
            svc => svc.CreatePaymentAsync(new CreatePaymentDto
            {
                BusinessOrderNo = "ORDER-TAX-INC",
                BusinessType = BusinessType.Order,
                Amount = 125m,
                Currency = "USD",
                ChannelCode = "Null"
            }));

        created.Succeeded.ShouldBeTrue();
        // 125 含 25% 税 → 税基 100，税额 25，应付额仍是标价 125
        created.Data!.Amount.ShouldBe(125m);
        created.Data.TaxAmount.ShouldBe(25m);
    }
}

/// <summary>
/// 零小数币种换算：JPY 的最小单位就是 1 日元，按 ×100 换算会多收 100 倍。
/// </summary>
public class CurrencyMinorUnitTests
{
    [Theory]
    [InlineData("USD", 12.34, 1234L)]
    [InlineData("EUR", 0.99, 99L)]
    [InlineData("JPY", 1200, 1200L)]
    [InlineData("KRW", 5000, 5000L)]
    [InlineData("KWD", 1.234, 1234L)]
    public void ToMinorUnits_RespectsCurrencyExponent(string currency, decimal amount, long expected)
    {
        CurrencyInfo.ToMinorUnits(amount, currency).ShouldBe(expected);
    }

    [Theory]
    [InlineData("USD", 1234L, 12.34)]
    [InlineData("JPY", 1200L, 1200)]
    [InlineData("KWD", 1234L, 1.234)]
    public void FromMinorUnits_RoundTrips(string currency, long minorUnits, decimal expected)
    {
        CurrencyInfo.FromMinorUnits(minorUnits, currency).ShouldBe(expected);
    }

    /// <summary>
    /// 不足一个最小单位的尾差必须进位而不是被截断，否则每笔都少收一点
    /// </summary>
    [Fact]
    public void ToMinorUnits_RoundsAwayFromZeroInsteadOfTruncating()
    {
        CurrencyInfo.ToMinorUnits(10.005m, "USD").ShouldBe(1001L);
        CurrencyInfo.ToMinorUnits(10.004m, "USD").ShouldBe(1000L);
    }
}
