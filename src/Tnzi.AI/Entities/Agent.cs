namespace Tnzi.AI.Entities;

/// <summary>
/// Agent 定义实体
/// </summary>
public class Agent : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// Agent 名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Agent 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 系统提示词
    /// </summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// 提供商名称
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// 模型名称
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 工具组列表（JSON 数组）
    /// </summary>
    public string? ToolGroups { get; set; }

    /// <summary>
    /// 温度参数
    /// </summary>
    public double? Temperature { get; set; }

    /// <summary>
    /// 最大 Token 数
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// 超时时间（秒）
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 执行模式
    /// </summary>
    public AgentExecutionMode ExecutionMode { get; set; } = AgentExecutionMode.Single;

    /// <summary>
    /// 额外配置（JSON）
    /// </summary>
    public string? Configuration { get; set; }

    // === 能力标签（v2.1 新增） ===

    /// <summary>领域标签（JSON 数组: ["coding", "research", "writing", "customer-service"]）</summary>
    public string? Domains { get; set; }

    /// <summary>角色标签（JSON 数组: ["reviewer", "implementer", "planner", "summarizer"]）</summary>
    public string? Roles { get; set; }

    /// <summary>质量等级 (1-5, 5=最高)</summary>
    public int QualityTier { get; set; } = 3;

    /// <summary>延迟等级 (1-5, 1=最快)</summary>
    public int LatencyTier { get; set; } = 3;

    /// <summary>成本等级 (1-5, 1=最便宜)</summary>
    public int CostTier { get; set; } = 3;
}
