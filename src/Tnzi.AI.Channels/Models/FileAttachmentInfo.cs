namespace Tnzi.AI.Channels.Models;

/// <summary>
/// 入站消息中的文件附件信息
/// </summary>
public record FileAttachmentInfo(
    string FileId,
    string FileName,
    string ContentType,
    long Size);
