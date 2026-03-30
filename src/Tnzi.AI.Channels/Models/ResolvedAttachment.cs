namespace Tnzi.AI.Channels.Models;

/// <summary>
/// 已解析的附件（可发送到 IM 平台）
/// </summary>
public record ResolvedAttachment(
    string VirtualPath,
    string ActualPath,
    string FileName,
    string ContentType,
    long Size,
    bool IsImage);
