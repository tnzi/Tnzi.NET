namespace Tnzi.Documents.Services.Internal;

/// <summary>
/// 「这份输入是不是本引擎该接的 HTML」——认领判定与入口校验。
/// </summary>
/// <remarks>
/// 出 PDF 与出缩略图对输入的要求逐字相同（同一个开关、同一份扩展名白名单、同一句错误提示），
/// 各写一份的结果一定是两边的提示语慢慢分叉，而提示语正是配错时唯一能指望的东西。
/// </remarks>
internal static class HtmlSource
{
    /// <summary>扩展名是不是 HTML，且浏览器渲染没被关掉。</summary>
    /// <param name="options">HTML 渲染配置。</param>
    /// <param name="fileName">源文件名（只取扩展名）。</param>
    public static bool IsHtml(HtmlPdfOptions options, string fileName)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(fileName))
            return false;

        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(extension) && DocumentFormats.HtmlExtensions.Contains(extension);
    }

    /// <summary>入口校验：空字节、开关关闭、扩展名不认，都在真去起浏览器之前拦下。</summary>
    /// <param name="options">HTML 渲染配置。</param>
    /// <param name="source">源文档字节。</param>
    /// <param name="sourceFileName">源文件名。</param>
    public static void Validate(HtmlPdfOptions options, byte[] source, string sourceFileName)
    {
        Check.NotNull(source);
        Check.NotNullOrWhiteSpace(sourceFileName);

        if (source.Length == 0)
            throw new DocumentConversionException($"Source document '{sourceFileName}' is empty.");

        if (!options.Enabled)
        {
            throw new DocumentConversionException(
                "Browser-based HTML rendering is disabled ('Documents:Html:Enabled' is false).");
        }

        if (!IsHtml(options, sourceFileName))
        {
            throw new DocumentConversionException(
                $"'{Path.GetExtension(sourceFileName)}' is not an HTML document. " +
                $"Supported: {string.Join(", ", DocumentFormats.HtmlExtensions.Order(StringComparer.Ordinal))}.");
        }
    }
}
