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

        if (options.MaxPdfTextChars is < 1000 or > 200000)
            AddError(errors, nameof(FinanceAiOptions.MaxPdfTextChars), "must be between 1000 and 200000.", "1000..200000");

        // 空数组是「用缺省清单」，["*"] 是「不拦」；空白条目则一定是配置写坏了（尾随逗号、空字符串），
        // 而它会静默地永不匹配任何内容类型，与上面两种表达都不同却同样安静。
        if (options.VisionContentTypes.Any(string.IsNullOrWhiteSpace))
        {
            AddError(errors, nameof(FinanceAiOptions.VisionContentTypes),
                "must not contain blank entries.", "e.g. [\"image/jpeg\", \"image/png\"]");
        }

        // "*" 与具体类型并列是自相矛盾的配置：按「不拦」执行会让后面那几项看起来生效却毫无作用。
        if (options.VisionContentTypes.Length > 1
            && options.VisionContentTypes.Any(t => string.Equals(t?.Trim(), FinanceAiOptions.AnyVisionContentType, StringComparison.Ordinal)))
        {
            AddError(errors, nameof(FinanceAiOptions.VisionContentTypes),
                "must be either [\"*\"] alone (no gate) or a list of content types.", "[\"*\"]");
        }
    }
}
