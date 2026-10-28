using Tnzi.Payment.Dtos;
using Tnzi.Payment.Options;
using Tnzi.Payment.Services;

namespace Tnzi.Payment.Tests;

/// <summary>
/// 内置计税给出按税种拆开的一行（收据上的「HST 13%」带登记号）；不计税时没有行。
/// </summary>
public class TaxLineTests
{
    private static DefaultPaymentTaxCalculator Calculator(TaxOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<TaxOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);
        return new DefaultPaymentTaxCalculator(monitor.Object);
    }

    [Fact]
    public async Task ASingleRate_IsOneNamedLineCarryingTheRegistrationNumber()
    {
        var calculator = Calculator(new TaxOptions { Enabled = true, DefaultTaxRate = 13m, Name = "HST", RegistrationNumber = "123456789 RT0001" });

        var result = await calculator.CalculateAsync(new TaxCalculationRequest { NetAmount = 300m, Currency = "CAD" });

        result.Data!.TaxAmount.ShouldBe(39m);
        var line = result.Data.TaxLines.ShouldHaveSingleItem();
        line.Name.ShouldBe("HST");
        line.Rate.ShouldBe(13m);
        line.Amount.ShouldBe(39m);
        line.RegistrationNumber.ShouldBe("123456789 RT0001");
    }

    [Fact]
    public async Task WithoutTax_ThereAreNoLines()
    {
        var calculator = Calculator(new TaxOptions { Enabled = false, DefaultTaxRate = 13m });

        var result = await calculator.CalculateAsync(new TaxCalculationRequest { NetAmount = 300m, Currency = "CAD" });

        result.Data!.TaxLines.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheDefaultName_IsTax()
    {
        var calculator = Calculator(new TaxOptions { Enabled = true, DefaultTaxRate = 5m });

        var result = await calculator.CalculateAsync(new TaxCalculationRequest { NetAmount = 100m, Currency = "CAD" });

        result.Data!.TaxLines.ShouldHaveSingleItem().Name.ShouldBe("Tax");
    }

    [Fact]
    public void TaxLines_RoundTripThroughThePaymentColumn()
    {
        var lines = PaymentService.ParseTaxLines("""[{"name":"GST","rate":5,"amount":15,"registrationNumber":"R1"},{"name":"PST","rate":7,"amount":21}]""");

        lines.Select(l => l.Name).ShouldBe(["GST", "PST"]);
        PaymentService.ParseTaxLines(null).ShouldBeEmpty();
        PaymentService.ParseTaxLines("not json").ShouldBeEmpty();
    }
}
