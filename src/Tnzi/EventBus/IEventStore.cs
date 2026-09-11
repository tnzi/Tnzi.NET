
namespace Tnzi.EventBus;

/// <summary>
/// 事件持久化存储接口（用于存储已发布的事件，支持事件重放和可靠性保证）
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// 保存事件
    /// </summary>
    /// <param name="event">事件对象</param>
    /// <param name="eventType">事件类型名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SaveEventAsync(IEvent @event, string eventType, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取未处理的事件
    /// </summary>
    /// <param name="count">获取数量</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>未处理的事件列表</returns>
    Task<IEnumerable<StoredEvent>> GetUnprocessedEventsAsync(int count = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// 标记事件为已处理（投递成功）
    /// </summary>
    /// <remarks>
    /// 实现 MUST 清除 <see cref="StoredEvent.LastError"/>：残留的错误信息会让一条
    /// 「失败几次后终于投递成功」的记录与死信记录长得一模一样，两者的处置完全不同。
    /// </remarks>
    /// <param name="eventId">事件ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task MarkAsProcessedAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 标记事件为死信（重试次数耗尽，不再投递）
    /// </summary>
    /// <remarks>
    /// 死信与「投递成功」是两回事：前者需要人工介入，后者可以按保留期删掉。
    /// 实现 MUST 保留 <paramref name="error"/>（<see cref="StoredEvent.IsDeadLetter"/> 据此判定），
    /// 并 MUST 让这些记录不被 <see cref="DeleteExpiredEventsAsync"/> 删除 ——
    /// 否则唯一的失败证据会随保留期一起消失。
    /// 默认实现退回 <see cref="MarkAsProcessedAsync"/>，保持既有实现可编译；
    /// 它只保证「不再重试」，不保证可辨认。
    /// </remarks>
    /// <param name="eventId">事件ID</param>
    /// <param name="error">最后一次失败的错误信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task MarkAsDeadLetterAsync(Guid eventId, string error, CancellationToken cancellationToken = default)
        => MarkAsProcessedAsync(eventId, cancellationToken);

    /// <summary>
    /// 标记事件为处理失败
    /// </summary>
    /// <param name="eventId">事件ID</param>
    /// <param name="error">错误信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task MarkAsFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取事件
    /// </summary>
    /// <param name="eventId">事件ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>存储的事件</returns>
    Task<StoredEvent?> GetEventAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取事件列表（分页）
    /// </summary>
    /// <param name="query">查询条件</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>分页的事件列表</returns>
    Task<IPagedList<StoredEvent>> GetEventsAsync(EventQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除过期事件
    /// </summary>
    /// <param name="days">保留天数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>删除的记录数</returns>
    Task<int> DeleteExpiredEventsAsync(int days = 90, CancellationToken cancellationToken = default);
}

/// <summary>
/// 存储的事件
/// </summary>
public class StoredEvent
{
    /// <summary>
    /// 获取或设置 事件ID
    /// </summary>
    public Guid EventId { get; set; }

    /// <summary>
    /// 获取或设置 事件类型名称
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 事件数据（JSON格式）
    /// </summary>
    public string EventData { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 事件发生时间
    /// </summary>
    public DateTime EventTime { get; set; }

    /// <summary>
    /// 获取或设置 是否已处理
    /// </summary>
    public bool IsProcessed { get; set; }

    /// <summary>
    /// 获取或设置 处理时间
    /// </summary>
    public DateTime? ProcessedTime { get; set; }

    /// <summary>
    /// 获取或设置 处理失败次数
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// 获取或设置 最后错误信息
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// 获取或设置 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 获取 是否为死信（重试耗尽、不再投递）
    /// </summary>
    /// <remarks>
    /// 判据是「已处理但仍带着错误信息」：投递成功会清掉 <see cref="LastError"/>，
    /// 因此不需要为死信新增一列（新增列意味着每个消费应用都要迁移一次）。
    /// </remarks>
    public bool IsDeadLetter => IsProcessed && !string.IsNullOrEmpty(LastError);
}

/// <summary>
/// 事件查询DTO
/// </summary>
public class EventQueryDto : PagedQuery
{
    /// <summary>
    /// 获取或设置 事件类型（可选）
    /// </summary>
    public string? EventType { get; set; }

    /// <summary>
    /// 获取或设置 是否已处理（可选）
    /// </summary>
    public bool? IsProcessed { get; set; }

    /// <summary>
    /// 获取或设置 开始时间（可选）
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// 获取或设置 结束时间（可选）
    /// </summary>
    public DateTime? EndTime { get; set; }
}