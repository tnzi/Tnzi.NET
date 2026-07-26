
namespace Tnzi.EventBus;

/// <summary>
/// 内存实现的死信队列(有界)
/// 用于开发和测试环境,生产环境建议使用持久化实现(如基于 Outbox 的事件存储)
/// 容量由 EventBusOptions.DeadLetterQueueCapacity 控制,超限时驱逐失败时间最旧的条目并记录警告,
/// 防止处理器持续失败导致内存无界增长;重启后内容丢失
/// </summary>
public class InMemoryEventDeadLetterQueue : IEventDeadLetterQueue
{
    private readonly ConcurrentDictionary<Guid, DeadLetterEvent> _deadLetters = new();
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

        var deadLetter = new DeadLetterEvent
        {
            EventId = @event.EventId,
            EventTypeName = typeof(TEvent).FullName ?? typeof(TEvent).Name,
            EventData = JsonSerializer.Serialize(@event, _jsonOptions),
            HandlerTypeName = handlerType.FullName ?? handlerType.Name,
            ExceptionMessage = exception.Message,
            ExceptionStackTrace = exception.StackTrace,
            FailedAt = DateTime.UtcNow,
            RetryCount = 0 // 初始重试次数为 0，实际重试次数由调用方设置
        };

        _deadLetters.TryAdd(@event.EventId, deadLetter);
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
            foreach (var oldest in _deadLetters.Values.OrderBy(e => e.FailedAt).Take(overflow))
            {
                if (_deadLetters.TryRemove(oldest.EventId, out _))
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
        _deadLetters.TryRemove(eventId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _deadLetters.Clear();
        return Task.CompletedTask;
    }
}
