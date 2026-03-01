namespace Tnzi.Imaging.Options;

/// <summary>
/// ImagingOptions 验证器
/// </summary>
public class ImagingOptionsValidator : OptionsValidatorBase<ImagingOptions>
{
    protected override void ValidateOptions(ImagingOptions options, List<string> errors)
    {
        var captcha = options.Captcha;

        if (captcha.FontSize < 10 || captcha.FontSize > 100)
            AddError(errors, nameof(captcha.FontSize), "FontSize must be between 10 and 100.");

        if (captcha.Height < 0 || captcha.Height > 500)
            AddError(errors, nameof(captcha.Height), "Height must be between 0 and 500.");

        if (captcha.RandomPointPercent < 0 || captcha.RandomPointPercent > 100)
            AddError(errors, nameof(captcha.RandomPointPercent), "RandomPointPercent must be between 0 and 100.");

        if (captcha.RandomLineCount < 0 || captcha.RandomLineCount > 50)
            AddError(errors, nameof(captcha.RandomLineCount), "RandomLineCount must be between 0 and 50.");

        if (captcha.ExpireMinutes < 1 || captcha.ExpireMinutes > 60)
            AddError(errors, nameof(captcha.ExpireMinutes), "ExpireMinutes must be between 1 and 60.");

        if (captcha.DefaultLength < 2 || captcha.DefaultLength > 10)
            AddError(errors, nameof(captcha.DefaultLength), "DefaultLength must be between 2 and 10.");
    }
}
