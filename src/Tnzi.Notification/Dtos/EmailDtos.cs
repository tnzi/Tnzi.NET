namespace Tnzi.Notification.Dtos;

/// <summary>
/// 邮件附件
/// </summary>
public class EmailAttachment
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
}

