
namespace Tnzi.EventBus;

/// <summary>
/// 内存实现的死信队列
/// 用于开发和测试环境，生产环境建议使用持久化实现
/// </summary>
public class InMemoryEventDeadLetterQueue : IEventDeadLetterQueue
{
    private readonly ConcurrentDictionary<Guid, DeadLetterEvent> _deadLetters = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
        return Task.CompletedTask;
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