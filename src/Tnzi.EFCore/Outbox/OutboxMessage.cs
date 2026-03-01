
namespace Tnzi.EFCore.Outbox;

/// <summary>
/// Outbox 消息实体（用于可靠事件投递的持久化存储）
/// 仅追加写入，无需软删除
/// </summary>
public class OutboxMessage : CreationAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 事件类型的完全限定名称
    /// </summary>
    public string EventType { get; set; } = null!;

    /// <summary>
    /// 获取或设置 事件数据（JSON 序列化）
    /// </summary>
    public string EventData { get; set; } = null!;

    /// <summary>
    /// 获取或设置 事件原始发生时间
    /// </summary>
    public DateTime EventTime { get; set; }

    /// <summary>
    /// 获取或设置 是否已处理
    /// </summary>
    public bool IsProcessed { get; set; }

    /// <summary>
    /// 获取或设置 处理完成时间
    /// </summary>
    public DateTime? ProcessedTime { get; set; }

    /// <summary>
    /// 获取或设置 处理失败次数
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// 获取或设置 最后一次错误信息
    /// </summary>
    public string? LastError { get; set; }
}
