namespace Tnzi.AI.Dtos;

/// <summary>
/// Agent 记忆条目 DTO（列表项）- 管理端为某个 Agent 预置/管理的长期记忆。
/// </summary>
public class AgentMemoryDto
{
    /// <summary>记忆条目 ID</summary>
    public Guid Id { get; set; }
    /// <summary>记忆内容</summary>
    public string Content { get; set; } = string.Empty;
    /// <summary>类别（preference / fact / decision / pattern / instruction …）</summary>
    public string? Category { get; set; }
    /// <summary>重要性 (0-1)，影响检索排序权重</summary>
    public double Importance { get; set; }
    /// <summary>来源（admin = 管理端预置；append/write = 运行时沉淀）</summary>
    public string? Source { get; set; }
    /// <summary>访问次数</summary>
    public int AccessCount { get; set; }
    /// <summary>最后访问时间</summary>
    public DateTime? LastAccessedTime { get; set; }
    /// <summary>创建时间</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// Agent 记忆列表查询 DTO
/// </summary>
public class AgentMemoryListQueryDto : PagedQueryDto
{
    protected override int DefaultPageSize => 20;

    /// <summary>按类别过滤（可选）</summary>
    public string? Category { get; set; }

    /// <summary>关键字过滤（匹配 Content）</summary>
    public string? Keyword { get; set; }
}

/// <summary>
/// 创建 Agent 记忆 DTO
/// </summary>
public class CreateAgentMemoryDto
{
    /// <summary>记忆内容</summary>
    [Required]
    [MaxLength(4000)]
    public string Content { get; set; } = string.Empty;

    /// <summary>类别（可选）</summary>
    [MaxLength(100)]
    public string? Category { get; set; }

    /// <summary>重要性 (0-1)，默认 0.5</summary>
    [Range(0, 1)]
    public double Importance { get; set; } = 0.5;
}

/// <summary>
/// 更新 Agent 记忆 DTO（字段可选）
/// </summary>
public class UpdateAgentMemoryDto
{
    /// <summary>记忆内容</summary>
    [MaxLength(4000)]
    public string? Content { get; set; }

    /// <summary>类别</summary>
    [MaxLength(100)]
    public string? Category { get; set; }

    /// <summary>重要性 (0-1)</summary>
    [Range(0, 1)]
    public double? Importance { get; set; }
}
