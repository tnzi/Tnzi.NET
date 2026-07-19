namespace Tnzi.Finance.Payroll.Tests;

/// <summary>
/// PayrollOptions 验证器
/// </summary>
public class PayrollOptionsValidatorTests
{
    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(PayrollOptions options)
        => new PayrollOptionsValidator().Validate(null, options);

    [Fact]
    public void Defaults_AreValid()
    {
        Validate(new PayrollOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void MaxEmployeesPerRun_TooSmall_Fails()
    {
        Validate(new PayrollOptions { MaxEmployeesPerRun = 0 }).Failed.ShouldBeTrue();
    }

    [Fact]
    public void FormulaMaxLength_OutOfRange_Fails()
    {
        Validate(new PayrollOptions { FormulaMaxLength = 0 }).Failed.ShouldBeTrue();
        Validate(new PayrollOptions { FormulaMaxLength = 5000 }).Failed.ShouldBeTrue();
    }

    [Fact]
    public void UndefinedYtdBasis_Fails()
    {
        Validate(new PayrollOptions { YtdBasis = (YtdBasis)99 }).Failed.ShouldBeTrue();
    }

    [Fact]
    public void OverlongPrefix_Fails()
    {
        Validate(new PayrollOptions { PayRunNumberPrefix = new string('P', 17) }).Failed.ShouldBeTrue();
    }
}
