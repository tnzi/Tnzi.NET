using Microsoft.Extensions.DependencyInjection;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 一笔支付把收据要印的事实带在自己身上：当时的税额拆分（税率日后改了也不变），以及付款用的卡。
/// </summary>
public class PaymentReceiptFactsTests : PaymentIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<TaxOptions>(o =>
        {
            o.Enabled = true;
            o.DefaultTaxRate = 13m;
            o.Name = "HST";
            o.RegistrationNumber = "123456789 RT0001";
        });
    }

    [Fact]
    public async Task AnOffSessionCharge_KeepsItsTaxSplitAndTheCardItWasChargedTo()
    {
        var charged = await InScopeAsync<IPaymentService, Result<PaymentDto>>(svc => svc.ChargeOffSessionAsync(new OffSessionChargeDto
        {
            BusinessOrderNo = "SUB-RECEIPT",
            BusinessType = BusinessType.Subscription,
            Amount = 300m,
            Currency = "CAD",
            ChannelCode = "Null",
            PaymentMethodToken = "pm_receipt",
            PaymentMethodBrand = "visa",
            PaymentMethodLast4 = "4242",
            UserId = Guid.NewGuid()
        }));
        charged.Succeeded.ShouldBeTrue(charged.Message);

        var payment = (await ReloadAsync<PaymentEntity>(charged.Data!.Id))!;
        payment.TaxAmount.ShouldBe(39m);
        payment.PaymentMethodBrand.ShouldBe("visa");
        payment.PaymentMethodLast4.ShouldBe("4242");

        var line = PaymentService.ParseTaxLines(payment.TaxLinesJson).ShouldHaveSingleItem();
        line.Name.ShouldBe("HST");
        line.Rate.ShouldBe(13m);
        line.Amount.ShouldBe(39m);
        line.RegistrationNumber.ShouldBe("123456789 RT0001");
    }
}
