
namespace Tnzi.EFCore.Outbox;

/// <summary>
/// 基于 EF Core 的事件存储实现（Outbox 模式）
/// 通过 IServiceProvider 动态解析 DbContext，支持任意应用 DbContext
/// </summary>
public class EfCoreEventStore : IEventStore
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EfCoreEventStore> _logger;
    private readonly TimeProvider _timeProvider;

    public EfCoreEventStore(IServiceProvider serviceProvider, ILogger<EfCoreEventStore> logger, TimeProvider? timeProvider = null)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task SaveEventAsync(IEvent @event, string eventType, CancellationToken cancellationToken = default)
    {
        Check.NotNull(@event);
        Check.NotNullOrWhiteSpace(eventType);

        var dbContext = GetDbContext();
        var message = new OutboxMessage
        {
            EventType = eventType,
            EventData = JsonSerializer.Serialize(@event, @event.GetType()),
            EventTime = @event.EventTime
        };

        dbContext.Set<OutboxMessage>().Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<StoredEvent>> GetUnprocessedEventsAsync(int count = 100, CancellationToken cancellationToken = default)
    {
        var dbContext = GetDbContext();
        var messages = await dbContext.Set<OutboxMessage>()
            .Where(m => !m.IsProcessed)
            .OrderBy(m => m.CreationTime)
            .Take(count)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return messages.Select(MapToStoredEvent);
    }

    public async Task MarkAsProcessedAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var dbContext = GetDbContext();
        var message = await dbContext.Set<OutboxMessage>()
            .FirstOrDefaultAsync(m => m.Id == eventId, cancellationToken);

        if (message != null)
        {
            message.IsProcessed = true;
            message.ProcessedTime = _timeProvider.GetUtcNow().UtcDateTime;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkAsFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default)
    {
        var dbContext = GetDbContext();
        var message = await dbContext.Set<OutboxMessage>()
            .FirstOrDefaultAsync(m => m.Id == eventId, cancellationToken);

        if (message != null)
        {
            message.FailureCount++;
            message.LastError = error;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<StoredEvent?> GetEventAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var dbContext = GetDbContext();
        var message = await dbContext.Set<OutboxMessage>()
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == eventId, cancellationToken);

        return message != null ? MapToStoredEvent(message) : null;
    }

    public async Task<IPagedList<StoredEvent>> GetEventsAsync(EventQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var dbContext = GetDbContext();
        var queryable = dbContext.Set<OutboxMessage>().AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.EventType))
            queryable = queryable.Where(m => m.EventType.Contains(query.EventType));

        if (query.IsProcessed.HasValue)
            queryable = queryable.Where(m => m.IsProcessed == query.IsProcessed.Value);

        if (query.StartTime.HasValue)
            queryable = queryable.Where(m => m.CreationTime >= query.StartTime.Value);

        if (query.EndTime.HasValue)
            queryable = queryable.Where(m => m.CreationTime <= query.EndTime.Value);

        var totalCount = await queryable.CountAsync(cancellationToken);

        var items = await queryable
            .OrderByDescending(m => m.CreationTime)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken);

        var storedEvents = items.Select(MapToStoredEvent).ToList();

        return new PagedList<StoredEvent>(storedEvents, query.PageIndex, query.PageSize, totalCount);
    }

    public async Task<int> DeleteExpiredEventsAsync(int days = 90, CancellationToken cancellationToken = default)
    {
        var dbContext = GetDbContext();
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-days);

        // 使用 ExecuteDeleteAsync 直接在数据库端批量删除，避免加载到内存
        var deleted = await dbContext.Set<OutboxMessage>()
            .Where(m => m.IsProcessed && m.CreationTime < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
            _logger.LogInformation("Deleted {Count} expired outbox messages older than {Days} days", deleted, days);

        return deleted;
    }

    /// <summary>
    /// 从 DI 容器动态解析应用的 DbContext
    /// </summary>
    private DbContext GetDbContext()
    {
        // 通过 IEntityManager 获取已注册的主 DbContext 类型
        var entityManager = _serviceProvider.GetService<IEntityManager>();
        if (entityManager != null)
        {
            var dbContextTypes = entityManager.GetAllDbContextTypes();
            if (dbContextTypes.Length > 0)
            {
                var dbContext = _serviceProvider.GetService(dbContextTypes[0]) as DbContext;
                if (dbContext != null) return dbContext;
            }
        }

        // 回退：直接尝试从 DI 解析 DbContext
        var fallbackContext = _serviceProvider.GetService<DbContext>();
        if (fallbackContext != null) return fallbackContext;

        throw new InvalidOperationException(
            "No DbContext registered. Ensure at least one DbContext is configured in the application.");
    }

    private static StoredEvent MapToStoredEvent(OutboxMessage message)
    {
        return new StoredEvent
        {
            EventId = message.Id,
            EventType = message.EventType,
            EventData = message.EventData,
            EventTime = message.EventTime,
            IsProcessed = message.IsProcessed,
            ProcessedTime = message.ProcessedTime,
            FailureCount = message.FailureCount,
            LastError = message.LastError,
            CreationTime = message.CreationTime
        };
    }
}
