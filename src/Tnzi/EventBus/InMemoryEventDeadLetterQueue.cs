
namespace Tnzi.EventBus;

/// <summary>
/// 内存实现的死信队列(有界)
/// 用于开发和测试环境,生产环境建议使用持久化实现(如基于 Outbox 的事件存储)
/// 容量由 EventBusOptions.DeadLetterQueueCapacity 控制,超限时驱逐失败时间最旧的条目并记录警告,
/// 防止处理器持续失败导致内存无界增长;重启后内容丢失
/// </summary>
/// <remarks>
/// 按「事件 + 处理器」记账:同一事件的多个处理器都失败时每个处理器各留一条(重放要知道是哪几个处理器没跑成),
/// 同一处理器对同一事件再次失败时以最近一次为准。<see cref="RemoveAsync"/> 移除该事件的全部条目。
/// </remarks>
public class InMemoryEventDeadLetterQueue : IEventDeadLetterQueue
{
    private readonly ConcurrentDictionary<(Guid EventId, string HandlerTypeName), DeadLetterEvent> _deadLetters = new();
    private readonly ILogger<InMemoryEventDeadLetterQueue>? _logger;
    private readonly int _capacity;
    private readonly object _evictionLock = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public InMemoryEventDeadLetterQueue(
        IOptions<EventBusOptions>? options = null,
        ILogger<InMemoryEventDeadLetterQueue>? logger = null)
    {
        var capacity = options?.Value.DeadLetterQueueCapacity ?? 1000;
        _capacity = capacity > 0 ? capacity : 1000;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task AddAsync<TEvent>(TEvent @event, Type handlerType, Exception exception, CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        Check.NotNull(@event);
        Check.NotNull(handlerType);
        Check.NotNull(exception);

        // 以运行时类型记账与序列化：总线按运行时类型派发，以 IEvent / 基类静态类型传进来的事件
        // 若按 TEvent 记，类型名是 IEvent、序列化只剩基类字段，重放时还原不出原事件
        var eventType = @event.GetType();
        var deadLetter = new DeadLetterEvent
        {
            EventId = @event.EventId,
            EventTypeName = eventType.FullName ?? eventType.Name,
            EventData = JsonSerializer.Serialize(@event, eventType, _jsonOptions),
            HandlerTypeName = handlerType.FullName ?? handlerType.Name,
            ExceptionMessage = exception.Message,
            ExceptionStackTrace = exception.StackTrace,
            FailedAt = DateTime.UtcNow,
            RetryCount = 0 // 内存实现不记录重试次数
        };

        _deadLetters[(deadLetter.EventId, deadLetter.HandlerTypeName)] = deadLetter;
        EvictIfOverCapacity();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 超出容量时驱逐失败时间最旧的条目
    /// 驱逐在锁内串行执行(仅超限时触发,不影响正常路径吞吐)
    /// </summary>
    private void EvictIfOverCapacity()
    {
        if (_deadLetters.Count <= _capacity)
            return;

        lock (_evictionLock)
        {
            var overflow = _deadLetters.Count - _capacity;
            if (overflow <= 0)
                return;

            var evicted = 0;
            foreach (var oldest in _deadLetters.OrderBy(e => e.Value.FailedAt).Take(overflow).ToList())
            {
                if (_deadLetters.TryRemove(oldest.Key, out _))
                {
                    evicted++;
                }
            }

            if (evicted > 0)
            {
                _logger?.LogWarning(
                    "In-memory dead letter queue exceeded capacity {Capacity}; evicted the {Evicted} oldest entries. " +
                    "Persistent handler failures detected, consider investigating failing handlers or using a persistent dead letter store.",
                    _capacity, evicted);
            }
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DeadLetterEvent>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var events = _deadLetters.Values.OrderByDescending(e => e.FailedAt).ToList();
        return Task.FromResult<IReadOnlyList<DeadLetterEvent>>(events);
    }

    /// <inheritdoc />
    public Task RemoveAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        foreach (var key in _deadLetters.Keys.Where(k => k.EventId == eventId).ToList())
        {
            _deadLetters.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _deadLetters.Clear();
        return Task.CompletedTask;
    }
}
