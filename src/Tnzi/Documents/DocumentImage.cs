namespace Tnzi.Documents;

/// <summary>
/// 渲染出来的位图：字节 + 内容类型 + 实际像素尺寸。
/// </summary>
/// <remarks>
/// 带上 <see cref="ContentType"/> 与实际尺寸，是因为调用方下一步几乎一定是「存成文件再喂给 <c>img</c>」：
/// 内容类型决定存储记录怎么标、响应头怎么发，而**实际**尺寸未必等于请求的尺寸
/// （裁剪高度超过整页时会被截断），布局要用真值。
/// </remarks>
public sealed class DocumentImage
{
    /// <summary>初始化一个 <see cref="DocumentImage"/> 实例。</summary>
    /// <param name="content">图片字节。</param>
    /// <param name="contentType">MIME 类型。</param>
    /// <param name="width">实际宽度（像素）。</param>
    /// <param name="height">实际高度（像素）。</param>
    public DocumentImage(byte[] content, string contentType, int width, int height)
    {
        Content = Check.NotNull(content);
        ContentType = Check.NotNullOrWhiteSpace(contentType);
        Width = width;
        Height = height;
    }

    /// <summary>图片字节。</summary>
    public byte[] Content { get; }

    /// <summary>MIME 类型（<c>image/png</c> / <c>image/jpeg</c> / <c>image/webp</c>）。</summary>
    public string ContentType { get; }

    /// <summary>实际宽度（像素）。</summary>
    public int Width { get; }

    /// <summary>实际高度（像素）。</summary>
    public int Height { get; }

    /// <summary>按内容类型给出的文件扩展名（含点号），存文件时用。</summary>
    public string FileExtension => ContentType switch
    {
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        _ => ".png"
    };
}
