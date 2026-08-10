namespace Tnzi.Finance.Ai.Options;

/// <summary>
/// Validator for <see cref="FinanceAiOptions"/>.
/// </summary>
public class FinanceAiOptionsValidator : OptionsValidatorBase<FinanceAiOptions>
{
    protected override void ValidateOptions(FinanceAiOptions options, List<string> errors)
    {
        if (options.MaxFileSizeMb is < 1 or > 100)
            AddError(errors, nameof(FinanceAiOptions.MaxFileSizeMb), "must be between 1 and 100.", "1..100");

        // 空数组是「不拦」的合法表达；空白条目则一定是配置写坏了（尾随逗号、空字符串），
        // 而它会静默地永不匹配任何内容类型 —— 与「不拦」的表现完全不同却同样安静。
        if (options.VisionContentTypes.Any(string.IsNullOrWhiteSpace))
        {
            AddError(errors, nameof(FinanceAiOptions.VisionContentTypes),
                "must not contain blank entries.", "e.g. [\"image/jpeg\", \"image/png\"]");
        }
    }
}
