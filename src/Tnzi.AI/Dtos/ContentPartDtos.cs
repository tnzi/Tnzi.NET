namespace Tnzi.AI.Dtos;

/// <summary>
/// 多模态内容部分基类
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextContentPartDto), "text")]
[JsonDerivedType(typeof(ImageContentPartDto), "image")]
[JsonDerivedType(typeof(FileContentPartDto), "file")]
[JsonDerivedType(typeof(DocumentContentPartDto), "document")]
public abstract class ContentPartDto
{
    // Type discriminator is handled automatically by [JsonPolymorphic] attribute.
    // Do NOT add a "Type" property here - it conflicts with the JSON metadata property.
}

/// <summary>
/// 文本内容部分
/// </summary>
public class TextContentPartDto : ContentPartDto
{
    /// <summary>Text content</summary>
    [Required]
    [MaxLength(10000)]
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// 图片内容部分（URL 或 Base64 二选一）
/// </summary>
public class ImageContentPartDto : ContentPartDto
{
    /// <summary>Image URL</summary>
    [MaxLength(2048)]
    public string? Url { get; set; }

    /// <summary>Base64 encoded image data</summary>
    public string? Base64Data { get; set; }

    /// <summary>Media type (e.g. image/png, image/jpeg)</summary>
    [MaxLength(100)]
    public string? MediaType { get; set; }

    /// <summary>Original file name, shown with the message when the attachment is stored</summary>
    [MaxLength(500)]
    public string? FileName { get; set; }
}

/// <summary>
/// 文档内容部分（内联 Base64）。目前接受的只有 PDF（<c>application/pdf</c>）。
/// </summary>
/// <remarks>
/// 与 <see cref="FileContentPartDto"/> 的区别：那个引用 Storage 里的文件（默认解析器不加载它），
/// 这个把文档字节随请求带来。内建引擎把它作为数据内容交给提供商（提供商不支持时以错误结束，不会被丢掉）；
/// 绑了外部 CLI 运行时的 Agent 把它作为 PDF 文档块交给 CLI，并随消息存档。
/// </remarks>
public class DocumentContentPartDto : ContentPartDto
{
    /// <summary>Base64 encoded document data (a <c>data:</c> URI is accepted as well)</summary>
    [Required]
    public string Base64Data { get; set; } = string.Empty;

    /// <summary>Media type, <c>application/pdf</c></summary>
    [MaxLength(100)]
    public string? MediaType { get; set; }

    /// <summary>Original file name, shown with the message</summary>
    [MaxLength(500)]
    public string? FileName { get; set; }
}

/// <summary>
/// 文件内容部分（引用 Storage 模块的文件）
/// </summary>
/// <remarks>
/// ★ 框架默认的 <c>IAgentResolver</c> <b>不加载</b>文件内容：带这种内容块的请求以 501
/// （<c>AI_UNSUPPORTED_MEDIA_TYPE</c>）失败，而不是把一行文件名交给一个看不到文件的模型。
/// 图片走 <see cref="ImageContentPartDto"/>；要支持文件引用，替换 <c>IAgentResolver</c>。
/// </remarks>
public class FileContentPartDto : ContentPartDto
{
    /// <summary>File ID from storage module</summary>
    [Required]
    public Guid FileId { get; set; }

    /// <summary>Original file name</summary>
    [MaxLength(500)]
    public string? FileName { get; set; }
}
