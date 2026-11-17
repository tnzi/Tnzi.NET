namespace Tnzi.AI.Options;

/// <summary>
/// Thread 配置选项验证器
/// </summary>
public class ThreadOptionsValidator : OptionsValidatorBase<ThreadOptions>
{
    protected override void ValidateOptions(ThreadOptions options, List<string> errors)
    {
        if (options.TitleMaxLength is < 10 or > 200)
        {
            errors.Add("TitleMaxLength must be between 10 and 200");
        }

        if (options.TitleTargetWords is < 1 or > 20)
        {
            errors.Add("TitleTargetWords must be between 1 and 20");
        }

        if (options.TitleTargetCjkCharacters is < 2 or > 40)
        {
            errors.Add("TitleTargetCjkCharacters must be between 2 and 40");
        }

        if (options.TitleMaxInputBytes is < 256 or > 65536)
        {
            errors.Add("TitleMaxInputBytes must be between 256 and 65536");
        }
    }
}
