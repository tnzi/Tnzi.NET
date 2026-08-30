namespace Tnzi.Documents.Options;

/// <summary>
/// <see cref="PdfRasterOptions"/> 的验证器。
/// </summary>
/// <remarks>
/// 只拦一种配置错误：把闸门开到一个小得没法渲染任何真实页面的值。
/// 那不是「更严格」，那是把整个能力配没了，而症状会是每一次渲染都失败。
/// </remarks>
public class PdfRasterOptionsValidator : OptionsValidatorBase<PdfRasterOptions>
{
    /// <summary>能画出任何有意义的东西所需的最小像素数（约 300×300）。</summary>
    private const long MinimumUsefulPagePixels = 90_000L;

    /// <inheritdoc />
    protected override void ValidateOptions(PdfRasterOptions options, List<string> errors)
    {
        if (options.MaxPagePixels < 0)
        {
            AddError(errors, nameof(PdfRasterOptions.MaxPagePixels), "cannot be negative (use 0 for no limit).");
        }
        else if (options.MaxPagePixels is > 0 and < MinimumUsefulPagePixels)
        {
            AddError(errors, nameof(PdfRasterOptions.MaxPagePixels),
                $"is {options.MaxPagePixels}, below the {MinimumUsefulPagePixels} pixels a readable page needs. Every render would fail. Use 0 for no limit.");
        }
    }
}
