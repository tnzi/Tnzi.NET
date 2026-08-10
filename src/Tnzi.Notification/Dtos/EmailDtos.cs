namespace Tnzi.Notification.Dtos;

/// <summary>
/// 邮件地址（地址 + 可选显示名）
/// </summary>
/// <param name="Address">邮箱地址</param>
/// <param name="Name">显示名；为空时信头里只写地址</param>
public sealed record EmailAddress(string Address, string? Name = null);

/// <summary>
/// 一封邮件：一次投递，可以同时寄给多个地址。
/// </summary>
/// <remarks>
/// 「一封信抄送多方」与「给每个人各发一封」是两种不同的消息：前者收件人彼此可见、回复归在同一线程，
/// 后者谁也不知道还写给了谁。正式函件里抄送名单本身就是信息，所以这里把 To/Cc/Bcc 作为同一封信的属性，
/// 而不是让调用方循环发送。
/// </remarks>
public class EmailMessage
{
    /// <summary>
    /// 获取或设置 主收件人
    /// </summary>
    public List<EmailAddress> To { get; set; } = [];

    /// <summary>
    /// 获取或设置 抄送（对 To 与 Cc 中的所有人可见）
    /// </summary>
    public List<EmailAddress> Cc { get; set; } = [];

    /// <summary>
    /// 获取或设置 密送（对任何其他收件人都不可见）
    /// </summary>
    public List<EmailAddress> Bcc { get; set; } = [];

    /// <summary>
    /// 获取或设置 邮件主题
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 邮件正文
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 正文是否为 HTML
    /// </summary>
    public bool IsHtml { get; set; } = true;

    /// <summary>
    /// 获取或设置 附件
    /// </summary>
    public List<EmailAttachment>? Attachments { get; set; }

    /// <summary>
    /// 创建一封只有单个收件人的邮件
    /// </summary>
    public static EmailMessage Create(string to, string? name = null, string subject = "", string body = "", bool isHtml = true, List<EmailAttachment>? attachments = null)
    {
        return new EmailMessage
        {
            To = [new EmailAddress(to, name)],
            Subject = subject,
            Body = body,
            IsHtml = isHtml,
            Attachments = attachments
        };
    }
}

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

