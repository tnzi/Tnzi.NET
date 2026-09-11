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

        if (options.MaxDecodePixels < 0)
            AddError(errors, nameof(options.MaxDecodePixels), "MaxDecodePixels cannot be negative (use 0 for no limit).");

        ValidateSlidingCaptcha(options.SlidingCaptcha, errors);
    }

    /// <summary>
    /// 校验滑动验证码配置。尺寸与拼图块大小必须留出滑动区间：
    /// 生成时用 Random.Next(pieceSize + 10, width - pieceSize - 10) 取缺口位置，
    /// 区间为空会直接抛 ArgumentOutOfRangeException（配置错误在启动时暴露，而不是在请求里）。
    /// </summary>
    private void ValidateSlidingCaptcha(SlidingCaptchaOptions sliding, List<string> errors)
    {
        if (sliding.PieceSize < 10)
            AddError(errors, "SlidingCaptcha.PieceSize", "PieceSize must be at least 10.");

        if (sliding.Width <= sliding.PieceSize * 2 + 20)
            AddError(errors, "SlidingCaptcha.Width",
                "Width must be greater than PieceSize * 2 + 20 to leave room for the puzzle gap and the slide range.");

        if (sliding.Height <= sliding.PieceSize + 20)
            AddError(errors, "SlidingCaptcha.Height",
                "Height must be greater than PieceSize + 20 to leave room for the puzzle gap.");

        // ★ 容差必须有上界，否则「验证」形同虚设：容差达到拼图块尺寸时，
        // 拼块放在缺口附近任何位置都算通过，而配置校验一声不响地放行 ——
        // 这个验证码看起来在工作，实际上谁都能过。此前只校验了下界。
        if (sliding.Tolerance < 0)
            AddError(errors, "SlidingCaptcha.Tolerance", "Tolerance must be greater than or equal to 0.");
        else if (sliding.Tolerance >= sliding.PieceSize)
            AddError(errors, "SlidingCaptcha.Tolerance",
                "Tolerance must be smaller than PieceSize; at or above it almost any slider position passes.");

        if (sliding.ExpirationMinutes < 1 || sliding.ExpirationMinutes > 60)
            AddError(errors, "SlidingCaptcha.ExpirationMinutes", "ExpirationMinutes must be between 1 and 60.");
    }
}
