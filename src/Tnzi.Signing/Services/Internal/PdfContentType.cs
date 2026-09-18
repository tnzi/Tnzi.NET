namespace Tnzi.Signing.Services.Internal;

/// <summary>
/// 一份存储记录声明的 Content-Type 是不是 PDF。
/// </summary>
/// <remarks>
/// <para>
/// 本模块经手的文档只有一种形态：渲染稿、成品、证书都是 PDF。<c>FileRecord.ContentType</c> 是按
/// <b>上传者给的文件名</b>算出来的（<c>.html → text/html</c>），而模板的渲染稿 id 来自请求体 ——
/// 收件人端点把它内联交给浏览器、密封器把它交给盖章器，两处都以「这是一份 PDF」为前提。
/// 入口（建模板）与出口（按令牌取文档）各问一次，同一口径。
/// </para>
/// <para>
/// 参数部分（<c>; charset=…</c>）与大小写不影响判定；<c>null</c> 与空串答否 —— 白名单的默认答案是「不是」。
/// </para>
/// </remarks>
internal static class PdfContentType
{
    /// <summary>PDF 的标准 MIME 类型。</summary>
    public const string MediaType = "application/pdf";

    /// <summary>声明的类型（可带参数）是不是 <c>application/pdf</c>。</summary>
    public static bool IsPdf(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return false;

        var mediaType = contentType.AsSpan().Trim();
        var separator = mediaType.IndexOf(';');
        if (separator >= 0)
            mediaType = mediaType[..separator].TrimEnd();

        return mediaType.Equals(MediaType, StringComparison.OrdinalIgnoreCase);
    }
}
