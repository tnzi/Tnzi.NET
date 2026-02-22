namespace Tnzi.Storage.Events;

/// <summary>
/// 文件删除请求事件
/// 在事务成功后异步处理物理文件删除
/// </summary>
public class FileDeleteRequestedEvent : EventBase
{
    /// <summary>
    /// 文件ID
    /// </summary>
    public Guid FileId { get; set; }

    /// <summary>
    /// 文件路径
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// 缩略图路径
    /// </summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>
    /// 存储提供商
    /// </summary>
    public string Provider { get; set; } = "Local";
}
