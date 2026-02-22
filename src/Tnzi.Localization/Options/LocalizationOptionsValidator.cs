namespace Tnzi.Localization.Options;

/// <summary>
/// 本地化配置选项验证器
/// </summary>
public class LocalizationOptionsValidator : OptionsValidatorBase<LocalizationOptions>
{
    protected override void ValidateOptions(LocalizationOptions options, List<string> errors)
    {
        if (options.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.DefaultCulture))
            {
                AddError(errors, nameof(options.DefaultCulture), "Default culture must be specified when localization is enabled.");
            }

            if (string.IsNullOrWhiteSpace(options.ResourcesPath))
            {
                AddError(errors, nameof(options.ResourcesPath), "Resources path must be specified when localization is enabled.");
            }
        }
    }

    protected override void CollectWarnings(LocalizationOptions options, List<string> warnings)
    {
        if (options.Enabled && (options.SupportedCultures == null || options.SupportedCultures.Length == 0))
        {
            AddWarning(warnings, nameof(options.SupportedCultures), "No supported cultures specified; defaulting to 'en'.");
        }
    }
}
