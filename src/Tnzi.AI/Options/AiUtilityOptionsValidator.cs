namespace Tnzi.AI.Options;

/// <summary>
/// AI Utility 配置选项验证器
/// </summary>
public class AiUtilityOptionsValidator : OptionsValidatorBase<AiUtilityOptions>
{
    protected override void ValidateOptions(AiUtilityOptions options, List<string> errors)
    {
        if (options.MaxTokens is < 1 or > 100000)
        {
            errors.Add("MaxTokens must be between 1 and 100000");
        }

        if (options.Temperature is < 0 or > 2)
        {
            errors.Add("Temperature must be between 0 and 2");
        }
    }
}
