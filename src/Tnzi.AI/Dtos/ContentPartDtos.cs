namespace Tnzi.AI.Dtos;

/// <summary>
/// 多模态内容部分基类
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextContentPartDto), "text")]
[JsonDerivedType(typeof(ImageContentPartDto), "image")]
[JsonDerivedType(typeof(FileContentPartDto), "file")]
public abstract class ContentPartDto
{
    // Type discriminator is handled automatically by [JsonPolymorphic] attribute.
    // Do NOT add a "Type" property here — it conflicts with the JSON metadata property.
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
}

/// <summary>
/// 文件内容部分（引用 Storage 模块的文件）
/// </summary>
public class FileContentPartDto : ContentPartDto
{
    /// <summary>File ID from storage module</summary>
    [Required]
    public Guid FileId { get; set; }

    /// <summary>Original file name</summary>
    [MaxLength(500)]
    public string? FileName { get; set; }
}
