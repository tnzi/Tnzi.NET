namespace Tnzi.Documents.Options;

/// <summary>
/// <see cref="HtmlPdfOptions"/> 的验证器。
/// </summary>
/// <remarks>
/// 与 <see cref="DocumentsOptionsValidator"/> 同一口径：显式配置的浏览器路径在**启动期**就校验存在性，
/// 配错了立刻在启动日志里说清楚；未配置（走自动探测）不在这里判定 —— 那是「可选能力未安装」，
/// 由 <c>DocumentsModule</c> 启动时输出警告、真去转换时抛异常。
/// </remarks>
public class HtmlPdfOptionsValidator : OptionsValidatorBase<HtmlPdfOptions>
{
    /// <inheritdoc />
    protected override void ValidateOptions(HtmlPdfOptions options, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(options.BrowserPath)
            && !File.Exists(options.BrowserPath)
            && !Directory.Exists(options.BrowserPath))
        {
            AddError(errors, nameof(HtmlPdfOptions.BrowserPath),
                $"points to '{options.BrowserPath}', which is neither an existing file nor an existing directory. Leave it empty to auto-detect Chrome, Edge or Chromium.");
        }

        // 显式宽高是一对：只给一个是配错了，此时按纸张名出图会让人以为「配了但没生效」
        var hasWidth = options.PaperWidthPt > 0;
        var hasHeight = options.PaperHeightPt > 0;
        if (hasWidth != hasHeight)
        {
            AddError(errors, nameof(HtmlPdfOptions.PaperWidthPt),
                "must be set together with PaperHeightPt (both greater than zero), or both left unset to use PaperSize.");
        }

        if (!hasWidth && !hasHeight && !PaperSizes.TryGet(options.PaperSize, out _))
        {
            AddError(errors, nameof(HtmlPdfOptions.PaperSize),
                $"is '{options.PaperSize}', which is not a known paper size.",
                string.Join(", ", PaperSizes.Names));
        }

        if (options.PaperWidthPt < 0 || options.PaperHeightPt < 0)
            AddError(errors, nameof(HtmlPdfOptions.PaperWidthPt), "must not be negative.");

        foreach (var (name, value) in new[]
                 {
                     (nameof(HtmlPdfOptions.MarginTopPt), options.MarginTopPt),
                     (nameof(HtmlPdfOptions.MarginRightPt), options.MarginRightPt),
                     (nameof(HtmlPdfOptions.MarginBottomPt), options.MarginBottomPt),
                     (nameof(HtmlPdfOptions.MarginLeftPt), options.MarginLeftPt)
                 })
        {
            if (value < 0)
                AddError(errors, name, "must not be negative.");
        }

        // 0.1–2.0 是浏览器自身的限制，超出范围它会直接拒绝打印
        if (options.Scale is < 0.1d or > 2.0d)
            AddError(errors, nameof(HtmlPdfOptions.Scale), "must be between 0.1 and 2.0.", "0.1..2.0");

        if (options.TimeoutSeconds is < 5 or > 600)
            AddError(errors, nameof(HtmlPdfOptions.TimeoutSeconds), "must be between 5 and 600 seconds.", "5..600");

        if (options.MaxConcurrency is < 1 or > 16)
            AddError(errors, nameof(HtmlPdfOptions.MaxConcurrency), "must be between 1 and 16.", "1..16");
    }
}
