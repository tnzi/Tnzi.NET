namespace Tnzi.Finance.Tests;

/// <summary>
/// FinanceOptions 验证器
/// </summary>
public class FinanceOptionsValidatorTests
{
    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(FinanceOptions options)
        => new FinanceOptionsValidator().Validate(null, options);

    [Fact]
    public void Defaults_AreValid()
    {
        Validate(new FinanceOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void EmptyBaseCurrency_Fails()
    {
        Validate(new FinanceOptions { BaseCurrency = " " }).Failed.ShouldBeTrue();
    }

    [Fact]
    public void NegativeRoundingTolerance_Fails()
    {
        Validate(new FinanceOptions { RoundingTolerance = -0.01m }).Failed.ShouldBeTrue();
    }

    [Fact]
    public void BaseCurrencyDecimals_OutOfRange_Fails()
    {
        Validate(new FinanceOptions { BaseCurrencyDecimals = 5 }).Failed.ShouldBeTrue();
        Validate(new FinanceOptions { BaseCurrencyDecimals = -1 }).Failed.ShouldBeTrue();
    }

    [Fact]
    public void JournalNumberPadding_OutOfRange_Fails()
    {
        Validate(new FinanceOptions { JournalNumberPadding = 13 }).Failed.ShouldBeTrue();
    }

    [Fact]
    public void MaxLinesPerEntry_TooSmall_Fails()
    {
        Validate(new FinanceOptions { MaxLinesPerEntry = 1 }).Failed.ShouldBeTrue();
    }
}
