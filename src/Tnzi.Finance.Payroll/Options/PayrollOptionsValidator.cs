namespace Tnzi.Finance.Payroll.Options;

/// <summary>
/// 薪酬子模块配置验证器
/// </summary>
public class PayrollOptionsValidator : OptionsValidatorBase<PayrollOptions>
{
    protected override void ValidateOptions(PayrollOptions options, List<string> errors)
    {
        if (options.PayRunNumberPrefix == null)
            errors.Add("PayRunNumberPrefix cannot be null (use an empty string for no prefix).");
        else if (options.PayRunNumberPrefix.Length > 16)
            errors.Add("PayRunNumberPrefix must not exceed 16 characters.");

        if (options.MaxEmployeesPerRun < 1)
            errors.Add("MaxEmployeesPerRun must be at least 1.");

        // 上限与实体列宽（公式/条件列 4000）对齐，防止合法配置写出被截断的公式
        if (options.FormulaMaxLength is < 1 or > 4000)
            errors.Add("FormulaMaxLength must be between 1 and 4000.");

        if (!Enum.IsDefined(options.YtdBasis))
            errors.Add("YtdBasis must be a defined value (CalendarYear or FiscalYear).");
    }
}
