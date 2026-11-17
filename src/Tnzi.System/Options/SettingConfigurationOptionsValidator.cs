namespace Tnzi.System.Options;

/// <summary>
/// <see cref="SettingConfigurationOptions"/> 配置验证器。
/// </summary>
public class SettingConfigurationOptionsValidator : OptionsValidatorBase<SettingConfigurationOptions>
{
    protected override void ValidateOptions(SettingConfigurationOptions options, List<string> errors)
    {
        if (options.InitialLoadRetryDelay <= TimeSpan.Zero)
        {
            errors.Add("InitialLoadRetryDelay must be positive.");
        }

        if (options.InitialLoadMaxRetryDelay < options.InitialLoadRetryDelay)
        {
            errors.Add("InitialLoadMaxRetryDelay must not be shorter than InitialLoadRetryDelay.");
        }
    }
}
