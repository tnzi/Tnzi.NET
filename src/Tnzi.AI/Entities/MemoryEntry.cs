namespace Tnzi.AI.Entities;

/// <summary>
/// 记忆条目实体 — 持久化 Agent 记忆内容
/// </summary>
public class MemoryEntry : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 记忆范围（如 "default", "project-x"）
    /// </summary>
    public string Scope { get; set; } = "default";

    /// <summary>
    /// 记忆内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 来源标识（如 "user-input", "auto-save"）
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// 关联用户 ID
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 关联 Agent ID
    /// </summary>
    public Guid? AgentId { get; set; }

    /// <summary>
    /// 嵌入向量（可空，向后兼容无嵌入场景）
    /// </summary>
    /// <remarks>
    /// 当 IEmbeddingService 可用时自动生成，用于混合搜索。
    /// 为 null 时降级为关键字搜索。
    /// </remarks>
    public float[]? EmbeddingVector { get; set; }

    /// <summary>
    /// 记忆重要性（0-1，越高越重要，默认 0.5）
    /// </summary>
    public double Importance { get; set; } = 0.5;

    /// <summary>
    /// 最后访问时间（搜索命中时更新）
    /// </summary>
    public DateTime? LastAccessedTime { get; set; }

    /// <summary>
    /// 访问次数（搜索命中时递增）
    /// </summary>
    public int AccessCount { get; set; }

    /// <summary>
    /// 记忆类别（preference, fact, decision, pattern, instruction）
    /// </summary>
    public string? Category { get; set; }
}
