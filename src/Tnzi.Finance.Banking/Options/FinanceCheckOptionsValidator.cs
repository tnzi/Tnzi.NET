namespace Tnzi.Finance.Banking.Options;

/// <summary>
/// 支票票面配置验证器
/// </summary>
/// <remarks>
/// ★ 启动期<b>拒绝</b>而不是静默回退：<see cref="Services.CheckNumberFormat.Normalize"/> 的兜底
/// 是给「消费应用自己构造渲染请求」那条路准备的，而配置写错了却照常启动，
/// 会让人以为自己配的位数生效了 —— 纸印出来才发现不是。
/// </remarks>
public class FinanceCheckOptionsValidator : OptionsValidatorBase<FinanceCheckOptions>
{
    protected override void ValidateOptions(FinanceCheckOptions options, List<string> errors)
    {
        if (options.CheckNumberDigits is < CheckNumberFormat.MinDigits or > CheckNumberFormat.MaxDigits)
        {
            errors.Add($"CheckNumberDigits must be between {CheckNumberFormat.MinDigits} and {CheckNumberFormat.MaxDigits} "
                + $"({CheckNumberFormat.MinDigits} means no zero padding).");
        }
    }
}
