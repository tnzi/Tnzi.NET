namespace Tnzi.AI.Dtos;

/// <summary>
/// 文件附件描述
/// </summary>
public record FileAttachment(string FileName, long Size, string? ContentType = null);
