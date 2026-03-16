namespace Tnzi.AI.Infrastructure.Memory;

/// <summary>
/// 数据库实体记忆存储 — 使用 EF Core 持久化命名实体记忆
/// </summary>
[ExperimentalApi(Reason = "Entity memory is in preview")]
public class DatabaseEntityMemoryStore : IEntityMemoryStore
{
    private readonly IRepository<EntityMemory, Guid> _repository;
    private readonly ILogger<DatabaseEntityMemoryStore> _logger;
    private readonly EntityMemoryOptions? _options;

    public DatabaseEntityMemoryStore(
        IRepository<EntityMemory, Guid> repository,
        ILogger<DatabaseEntityMemoryStore> logger,
        IOptions<AIOptions>? aiOptions = null)
    {
        _repository = Check.NotNull(repository);
        _logger = Check.NotNull(logger);
        _options = aiOptions?.Value.ContextProviders.EntityMemory;
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
        if (_options?.EntityExpiration.HasValue == true)
        {
            var cutoff = DateTime.UtcNow - _options.EntityExpiration.Value;
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

        var existing = await _repository.AsQueryable()
            .FirstOrDefaultAsync(e => e.EntityName == entry.EntityName && e.UserId == entry.UserId && e.AgentId == entry.AgentId, ct);

        if (existing != null)
        {
            // 更新已有实体：累加提及次数、合并属性、更新最后提及时间
            existing.MentionCount += Math.Max(entry.MentionCount, 1);
            existing.LastMentioned = entry.LastMentioned != default ? entry.LastMentioned : DateTime.UtcNow;
            existing.EntityType = entry.EntityType;
            existing.Properties = SerializeProperties(MergeProperties(
                DeserializeProperties(existing.Properties), entry.Properties));

            await _repository.UpdateAsync(existing);
            _logger.LogDebug("Updated entity memory: {EntityName} (mentions: {MentionCount})",
                existing.EntityName, existing.MentionCount);
        }
        else
        {
            // 插入新实体
            var entity = new EntityMemory
            {
                EntityName = entry.EntityName,
                EntityType = entry.EntityType,
                Properties = SerializeProperties(entry.Properties),
                LastMentioned = entry.LastMentioned != default ? entry.LastMentioned : DateTime.UtcNow,
                MentionCount = Math.Max(entry.MentionCount, 1),
                UserId = entry.UserId,
                AgentId = entry.AgentId
            };

            await _repository.InsertAsync(entity);
            _logger.LogDebug("Inserted entity memory: {EntityName} ({EntityType})",
                entry.EntityName, entry.EntityType);
        }
    }

    /// <inheritdoc />
    public async Task UpsertEntitiesAsync(IEnumerable<EntityMemoryEntry> entries, CancellationToken ct = default)
    {
        Check.NotNull(entries);

        var entryList = entries.ToList();
        if (entryList.Count == 0) return;

        // 单条时走原有逻辑避免额外查询开销
        if (entryList.Count == 1)
        {
            await UpsertEntityAsync(entryList[0], ct);
            return;
        }

        // 批量查询: 提取所有 EntityName 一次查询
        var names = entryList.Select(e => e.EntityName).Distinct().ToList();
        var userIds = entryList.Select(e => e.UserId).Distinct().ToList();

        var existingEntities = await _repository.AsQueryable()
            .Where(e => names.Contains(e.EntityName) && userIds.Contains(e.UserId))
            .ToListAsync(ct);

        // 按 (EntityName, UserId, AgentId) 建索引
        var existingDict = existingEntities
            .GroupBy(e => (e.EntityName, e.UserId, e.AgentId))
            .ToDictionary(g => g.Key, g => g.First());

        var toUpdate = new List<EntityMemory>();
        var toInsert = new List<EntityMemory>();

        foreach (var entry in entryList)
        {
            Check.NotNullOrWhiteSpace(entry.EntityName);

            var key = (entry.EntityName, entry.UserId, entry.AgentId);
            if (existingDict.TryGetValue(key, out var existing))
            {
                // 更新
                existing.MentionCount += Math.Max(entry.MentionCount, 1);
                existing.LastMentioned = entry.LastMentioned != default ? entry.LastMentioned : DateTime.UtcNow;
                existing.EntityType = entry.EntityType;
                existing.Properties = SerializeProperties(MergeProperties(
                    DeserializeProperties(existing.Properties), entry.Properties));
                toUpdate.Add(existing);
            }
            else
            {
                // 插入
                var entity = new EntityMemory
                {
                    EntityName = entry.EntityName,
                    EntityType = entry.EntityType,
                    Properties = SerializeProperties(entry.Properties),
                    LastMentioned = entry.LastMentioned != default ? entry.LastMentioned : DateTime.UtcNow,
                    MentionCount = Math.Max(entry.MentionCount, 1),
                    UserId = entry.UserId,
                    AgentId = entry.AgentId
                };
                toInsert.Add(entity);
                // 避免同批次重复插入
                existingDict[key] = entity;
            }
        }

        if (toUpdate.Count > 0)
        {
            await _repository.UpdateManyAsync(toUpdate);
            _logger.LogDebug("Batch updated {Count} entity memories", toUpdate.Count);
        }

        if (toInsert.Count > 0)
        {
            await _repository.InsertManyAsync(toInsert);
            _logger.LogDebug("Batch inserted {Count} entity memories", toInsert.Count);
        }
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
