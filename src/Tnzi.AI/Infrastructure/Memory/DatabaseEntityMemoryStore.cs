namespace Tnzi.AI.Infrastructure.Memory;

/// <summary>
/// 数据库实体记忆存储 - 使用 EF Core 持久化命名实体记忆
/// </summary>
public class DatabaseEntityMemoryStore : IEntityMemoryStore
{
    private readonly IRepository<EntityMemory, Guid> _repository;
    private readonly ILogger<DatabaseEntityMemoryStore> _logger;
    private readonly IOptionsMonitor<AIOptions>? _aiOptions;

    private EntityMemoryOptions? Options => _aiOptions?.CurrentValue.ContextProviders.EntityMemory;

    public DatabaseEntityMemoryStore(
        IRepository<EntityMemory, Guid> repository,
        ILogger<DatabaseEntityMemoryStore> logger,
        IOptionsMonitor<AIOptions>? aiOptions = null)
    {
        _repository = Check.NotNull(repository);
        _logger = Check.NotNull(logger);
        _aiOptions = aiOptions;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EntityMemoryEntry>> GetRelevantEntitiesAsync(Guid? userId, int maxEntities = 20, CancellationToken ct = default)
        => GetRelevantEntitiesAsync(userId, agentId: null, maxEntities, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<EntityMemoryEntry>> GetRelevantEntitiesAsync(Guid? userId, Guid? agentId, int maxEntities = 20, CancellationToken ct = default)
    {
        var query = _repository.AsQueryable();

        query = userId.HasValue
            ? query.Where(e => e.UserId == userId.Value)
            : query.Where(e => e.UserId == null);

        if (agentId.HasValue)
        {
            query = query.Where(e => e.AgentId == agentId.Value);
        }

        // 过期过滤
        var options = Options;
        if (options?.EntityExpiration.HasValue == true)
        {
            var cutoff = DateTime.UtcNow - options.EntityExpiration.Value;
            query = query.Where(e => e.LastMentioned >= cutoff);
        }

        var entities = await query
            .OrderByDescending(e => e.LastMentioned)
            .Take(maxEntities)
            .ToListAsync(ct);

        return entities.Select(MapToEntry).ToList();
    }

    /// <inheritdoc />
    public async Task UpsertEntityAsync(EntityMemoryEntry entry, CancellationToken ct = default)
    {
        Check.NotNull(entry);
        Check.NotNullOrWhiteSpace(entry.EntityName);

        var now = entry.LastMentioned != default ? entry.LastMentioned : DateTime.UtcNow;
        var mentionIncrement = Math.Max(entry.MentionCount, 1);

        // 先读取已有属性用于合并（只查 Properties 字段，轻量查询）
        var existingProps = await _repository.AsQueryable()
            .Where(e => e.EntityName == entry.EntityName && e.UserId == entry.UserId && e.AgentId == entry.AgentId)
            .Select(e => e.Properties)
            .FirstOrDefaultAsync(ct);

        if (existingProps != null)
        {
            // 合并属性后用 ExecuteUpdateAsync 直接执行 SQL UPDATE，绕过 ChangeTracker
            var mergedProps = SerializeProperties(MergeProperties(DeserializeProperties(existingProps), entry.Properties));
            await _repository.AsQueryable()
                .Where(e => e.EntityName == entry.EntityName && e.UserId == entry.UserId && e.AgentId == entry.AgentId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.MentionCount, e => e.MentionCount + mentionIncrement)
                    .SetProperty(e => e.LastMentioned, now)
                    .SetProperty(e => e.EntityType, entry.EntityType)
                    .SetProperty(e => e.Properties, mergedProps), ct);

            _logger.LogDebug("Updated entity memory: {EntityName}", entry.EntityName);
            return;
        }

        var serializedProps = SerializeProperties(entry.Properties);

        // 不存在则插入；如果并发冲突导致唯一约束失败，重试一次 UPDATE
        // 注意: 极端并发下 InsertAsync 失败会在 ChangeTracker 留下脏实体（Added 状态），
        // 可能导致同作用域后续 SaveChangesAsync 连带失败。此情况需要同一用户对同一 Agent
        // 同时触发相同新实体提取，概率极低，且 History/UsageLogging 均有 silent try-catch 兜底。
        try
        {
            var entity = new EntityMemory
            {
                EntityName = entry.EntityName,
                EntityType = entry.EntityType,
                Properties = serializedProps,
                LastMentioned = now,
                MentionCount = mentionIncrement,
                UserId = entry.UserId,
                AgentId = entry.AgentId
            };

            await _repository.InsertAsync(entity);
            _logger.LogDebug("Inserted entity memory: {EntityName} ({EntityType})", entry.EntityName, entry.EntityType);
        }
        catch (DbUpdateException)
        {
            // 并发插入导致唯一约束冲突 - 回退到 UPDATE（绕过 ChangeTracker）
            await _repository.AsQueryable()
                .Where(e => e.EntityName == entry.EntityName && e.UserId == entry.UserId && e.AgentId == entry.AgentId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.MentionCount, e => e.MentionCount + mentionIncrement)
                    .SetProperty(e => e.LastMentioned, now)
                    .SetProperty(e => e.EntityType, entry.EntityType)
                    .SetProperty(e => e.Properties, serializedProps), ct);

            _logger.LogDebug("Updated entity memory after conflict: {EntityName}", entry.EntityName);
        }
    }

    /// <inheritdoc />
    public async Task UpsertEntitiesAsync(IEnumerable<EntityMemoryEntry> entries, CancellationToken ct = default)
    {
        Check.NotNull(entries);

        var entryList = entries.ToList();
        if (entryList.Count == 0) return;

        // 逐条 Upsert: 使用 ExecuteUpdateAsync 绕过 ChangeTracker，
        // 避免批量 InsertManyAsync 失败时污染 DbContext 导致连锁故障
        foreach (var entry in entryList)
        {
            await UpsertEntityAsync(entry, ct);
        }

        _logger.LogDebug("Upserted {Count} entity memories", entryList.Count);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EntityMemoryEntry>> SearchEntitiesAsync(string query, Guid? userId, int maxResults = 10, CancellationToken ct = default)
        => SearchEntitiesAsync(query, userId, agentId: null, maxResults, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<EntityMemoryEntry>> SearchEntitiesAsync(string query, Guid? userId, Guid? agentId, int maxResults = 10, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(query);

        var queryLower = query.ToLower();

        var dbQuery = _repository.AsQueryable();

        dbQuery = userId.HasValue
            ? dbQuery.Where(e => e.UserId == userId.Value)
            : dbQuery.Where(e => e.UserId == null);

        if (agentId.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.AgentId == agentId.Value);
        }

        var entities = await dbQuery
            .Where(e => e.EntityName.ToLower().Contains(queryLower)
                || e.EntityType.ToLower().Contains(queryLower))
            .OrderByDescending(e => e.MentionCount)
            .Take(maxResults)
            .ToListAsync(ct);

        return entities.Select(MapToEntry).ToList();
    }

    /// <summary>
    /// 将 EntityMemory 实体映射为 EntityMemoryEntry
    /// </summary>
    private static EntityMemoryEntry MapToEntry(EntityMemory entity)
    {
        return new EntityMemoryEntry
        {
            EntityName = entity.EntityName,
            EntityType = entity.EntityType,
            Properties = DeserializeProperties(entity.Properties),
            LastMentioned = entity.LastMentioned,
            MentionCount = entity.MentionCount,
            UserId = entity.UserId,
            AgentId = entity.AgentId
        };
    }

    /// <summary>
    /// 合并属性字典（新属性覆盖旧属性）
    /// </summary>
    private static Dictionary<string, string> MergeProperties(Dictionary<string, string> existing, Dictionary<string, string> incoming)
    {
        var merged = new Dictionary<string, string>(existing);
        foreach (var kvp in incoming)
        {
            merged[kvp.Key] = kvp.Value;
        }
        return merged;
    }

    /// <summary>
    /// 序列化属性字典为 JSON
    /// </summary>
    private static string SerializeProperties(Dictionary<string, string> properties)
    {
        return JsonSerializer.Serialize(properties);
    }

    /// <summary>
    /// 反序列化 JSON 为属性字典
    /// </summary>
    private static Dictionary<string, string> DeserializeProperties(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}
