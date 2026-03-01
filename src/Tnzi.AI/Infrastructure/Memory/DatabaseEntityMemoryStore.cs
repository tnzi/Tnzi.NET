namespace Tnzi.AI.Infrastructure.Memory;

/// <summary>
/// 数据库实体记忆存储 — 使用 EF Core 持久化命名实体记忆
/// </summary>
[ExperimentalApi(Reason = "Entity memory is in preview")]
public class DatabaseEntityMemoryStore : IEntityMemoryStore
{
    private readonly IRepository<EntityMemory, Guid> _repository;
    private readonly ILogger<DatabaseEntityMemoryStore> _logger;

    public DatabaseEntityMemoryStore(
        IRepository<EntityMemory, Guid> repository,
        ILogger<DatabaseEntityMemoryStore> logger)
    {
        _repository = Check.NotNull(repository);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EntityMemoryEntry>> GetRelevantEntitiesAsync(Guid? userId, int maxEntities = 20, CancellationToken ct = default)
    {
        var query = _repository.AsQueryable();

        query = userId.HasValue
            ? query.Where(e => e.UserId == userId.Value)
            : query.Where(e => e.UserId == null);

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
            .FirstOrDefaultAsync(e => e.EntityName == entry.EntityName && e.UserId == entry.UserId, ct);

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
                UserId = entry.UserId
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

        foreach (var entry in entries)
        {
            await UpsertEntityAsync(entry, ct);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EntityMemoryEntry>> SearchEntitiesAsync(string query, Guid? userId, int maxResults = 10, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(query);

        var queryLower = query.ToLower();

        var dbQuery = _repository.AsQueryable();

        dbQuery = userId.HasValue
            ? dbQuery.Where(e => e.UserId == userId.Value)
            : dbQuery.Where(e => e.UserId == null);

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
            UserId = entity.UserId
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
