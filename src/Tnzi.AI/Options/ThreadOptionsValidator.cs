namespace Tnzi.AI.Options;

/// <summary>
/// Thread 配置选项验证器
/// </summary>
public class ThreadOptionsValidator : OptionsValidatorBase<ThreadOptions>
{
    protected override void ValidateOptions(ThreadOptions options, List<string> errors)
    {
        if (options.TitleMaxLength is < 10 or > 500)
        {
            errors.Add("TitleMaxLength must be between 10 and 500");
        }
    }
}
