namespace Tnzi.AI.Memory;

/// <summary>
/// 实体记忆存储接口 — 跨会话追踪命名实体（人物、组织、地点、概念等）
/// </summary>
[ExperimentalApi(Reason = "Entity memory is in preview")]
public interface IEntityMemoryStore
{
    /// <summary>
    /// 获取用户的相关实体记忆（按最近提及时间排序）
    /// </summary>
    /// <param name="userId">用户 ID（null 表示全局）</param>
    /// <param name="maxEntities">最大返回数量</param>
    /// <param name="ct">取消令牌</param>
    Task<IReadOnlyList<EntityMemoryEntry>> GetRelevantEntitiesAsync(Guid? userId, int maxEntities = 20, CancellationToken ct = default);

    /// <summary>
    /// 插入或更新实体记忆（按 EntityName + UserId 匹配）
    /// </summary>
    /// <param name="entry">实体记忆条目</param>
    /// <param name="ct">取消令牌</param>
    Task UpsertEntityAsync(EntityMemoryEntry entry, CancellationToken ct = default);

    /// <summary>
    /// 批量插入或更新实体记忆
    /// </summary>
    /// <param name="entries">实体记忆条目列表</param>
    /// <param name="ct">取消令牌</param>
    Task UpsertEntitiesAsync(IEnumerable<EntityMemoryEntry> entries, CancellationToken ct = default);

    /// <summary>
    /// 搜索实体记忆（按名称关键词匹配）
    /// </summary>
    /// <param name="query">搜索关键词</param>
    /// <param name="userId">用户 ID（null 表示全局）</param>
    /// <param name="maxResults">最大结果数</param>
    /// <param name="ct">取消令牌</param>
    Task<IReadOnlyList<EntityMemoryEntry>> SearchEntitiesAsync(string query, Guid? userId, int maxResults = 10, CancellationToken ct = default);
}

/// <summary>
/// 实体记忆条目
/// </summary>
[ExperimentalApi(Reason = "Entity memory is in preview")]
public class EntityMemoryEntry
{
    /// <summary>
    /// 实体名称（如 "张三"、"微软"、"北京"）
    /// </summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>
    /// 实体类型（person, organization, location, concept）
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// 实体属性字典（如 { "role": "CTO", "company": "微软" }）
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();

    /// <summary>
    /// 最后一次提及时间
    /// </summary>
    public DateTime LastMentioned { get; set; }

    /// <summary>
    /// 提及次数
    /// </summary>
    public int MentionCount { get; set; }

    /// <summary>
    /// 关联用户 ID
    /// </summary>
    public Guid? UserId { get; set; }
}
