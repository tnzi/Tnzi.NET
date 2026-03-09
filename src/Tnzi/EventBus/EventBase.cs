namespace Tnzi.EventBus;

/// <summary>
/// 事件基类
/// </summary>
public abstract class EventBase : IEvent
{
    /// <summary>
    /// 获取或设置 事件ID
    /// </summary>
    public Guid EventId { get; set; } = SequentialGuid.NewGuid();

    /// <summary>
    /// 获取或设置 事件发生时间
    /// </summary>
    public DateTime EventTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 获取或设置 事件源（触发事件的对象，用于调试和审计）
    /// </summary>
    public object? EventSource { get; set; }

    /// <summary>
    /// 获取或设置 租户 ID（发布时自动捕获，后台处理器用于恢复租户上下文）
    /// </summary>
    public Guid? TenantId { get; set; }
}

