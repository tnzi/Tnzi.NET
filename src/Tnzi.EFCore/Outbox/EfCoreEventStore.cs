
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

        var dbContext = GetDbContext();
        var actualType = @event.GetType();

        // 始终使用运行时类型的 AssemblyQualifiedName 存储，确保 Type.GetType() 跨程序集解析正确
        // 调用方传入的 eventType 可作为查询/路由用途，但序列化存储必须用完整类型名
        var resolvedEventType = actualType.AssemblyQualifiedName ?? actualType.FullName!;

        var message = new OutboxMessage
        {
            EventType = resolvedEventType,
            EventData = JsonSerializer.Serialize(@event, actualType),
            EventTime = @event.EventTime
        };

        dbContext.Set<OutboxMessage>().Add(message);

        // 在 UoW 事务内时，不主动提交——由 UoW 统一提交，保证 OutboxMessage 与业务数据原子性
        // 没有 UoW 时（独立调用场景），自己提交
        var uowManager = _serviceProvider.GetService<IUnitOfWorkManager>();
        if (uowManager?.IsEnabledTransaction != true)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
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
        var processedTime = _timeProvider.GetUtcNow().UtcDateTime;

        await dbContext.Set<OutboxMessage>()
            .Where(m => m.Id == eventId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.IsProcessed, true)
                .SetProperty(m => m.ProcessedTime, processedTime),
                cancellationToken);
    }

    public async Task MarkAsFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default)
    {
        var dbContext = GetDbContext();

        // 使用 ExecuteUpdateAsync 原子递增 FailureCount，避免 Load-then-Update 的并发竞争
        await dbContext.Set<OutboxMessage>()
            .Where(m => m.Id == eventId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.FailureCount, m => m.FailureCount + 1)
                .SetProperty(m => m.LastError, error),
                cancellationToken);
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
        // 优先使用 Primary DbContext（注册为基类 DbContext 的那个）
        // Outbox 是跨切面基础设施，应该始终使用主数据库
        var primaryContext = _serviceProvider.GetService<DbContext>();
        if (primaryContext != null) return primaryContext;

        // 回退：通过 IEntityManager 遍历所有有显式 DbContextType 注册的 DbContext，
        // 找到包含 OutboxMessage 实体的那个
        // 注意：模块专属 DbContext（如 RagDbContext）不包含 OutboxMessage，需要跳过
        var entityManager = _serviceProvider.GetService<IEntityManager>();
        if (entityManager != null)
        {
            var dbContextTypes = entityManager.GetAllDbContextTypes();
            foreach (var contextType in dbContextTypes)
            {
                if (_serviceProvider.GetService(contextType) is not DbContext dbContext)
                    continue;

                if (dbContext.Model.FindEntityType(typeof(OutboxMessage)) != null)
                    return dbContext;
            }
        }

        throw new InvalidOperationException(
            "No DbContext containing OutboxMessage was found. " +
            "Ensure the primary DbContext is registered via AddTnziDbContext(isPrimary: true), " +
            "or configure Database:DbContexts in appsettings.json.");
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
