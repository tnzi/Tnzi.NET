namespace Tnzi.Payment.Billing.Tests.Integration;

/// <summary>
/// <c>Payment:Invoice:Seller:AccentColor</c> 写进模板的样式表，启动期只放行十六进制颜色。
/// </summary>
public class InvoiceAccentColorValidationTests
{
    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(string? accent)
        => new InvoiceOptionsValidator().Validate(null, new InvoiceOptions { Seller = new InvoiceSellerOptions { AccentColor = accent } });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#0B5FFF")]
    [InlineData("#06f")]
    [InlineData(" #4f46e5 ")]
    public void HexColoursAndNoColour_AreAccepted(string? accent)
        => Validate(accent).Succeeded.ShouldBeTrue();

    [Theory]
    [InlineData("blue")]
    [InlineData("0B5FFF")]
    [InlineData("#0B5FF")]
    [InlineData("#GGGGGG")]
    [InlineData("#000; } body { display: none")]
    public void AnythingElse_FailsAtStartup_NamingTheKey(string accent)
    {
        var result = Validate(accent);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Payment:Invoice:Seller:AccentColor");
    }
}
