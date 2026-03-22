namespace Tnzi.Notification.Dtos;

/// <summary>
/// 邮件附件
/// </summary>
public class EmailAttachment
{
    /// <summary>
    /// 附件文件名（必填）
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件路径或 URL（与 Content 二选一）
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// 内存附件数据（与 FilePath 二选一，优先级高于 FilePath）
    /// </summary>
    public byte[]? Content { get; set; }

    /// <summary>
    /// MIME 类型
    /// </summary>
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>
    /// 从文件路径或 URL 创建附件
    /// </summary>
    public static EmailAttachment FromFile(string filePath, string? fileName = null, string contentType = "application/octet-stream")
    {
        Check.NotNullOrWhiteSpace(filePath);
        return new EmailAttachment
        {
            FileName = fileName ?? Path.GetFileName(filePath),
            FilePath = filePath,
            ContentType = contentType
        };
    }

    /// <summary>
    /// 从内存数据创建附件
    /// </summary>
    public static EmailAttachment FromBytes(byte[] content, string fileName, string contentType = "application/octet-stream")
    {
        Check.NotNull(content);
        Check.NotNullOrWhiteSpace(fileName);
        return new EmailAttachment
        {
            FileName = fileName,
            Content = content,
            ContentType = contentType
        };
    }

    /// <summary>
    /// 从 Stream 创建附件（会立即读取到内存）
    /// </summary>
    public static async Task<EmailAttachment> FromStreamAsync(Stream stream, string fileName, string contentType = "application/octet-stream")
    {
        Check.NotNull(stream);
        Check.NotNullOrWhiteSpace(fileName);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return new EmailAttachment
        {
            FileName = fileName,
            Content = ms.ToArray(),
            ContentType = contentType
        };
    }
}

