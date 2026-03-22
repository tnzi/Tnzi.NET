namespace Tnzi.AI.Dtos;

/// <summary>
/// Agent 信息 DTO
/// </summary>
public class AgentDto
{
    /// <summary>Agent ID</summary>
    public Guid Id { get; set; }
    /// <summary>Agent name</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Agent description</summary>
    public string? Description { get; set; }
    /// <summary>System instructions</summary>
    public string? Instructions { get; set; }
    /// <summary>Provider name</summary>
    public string Provider { get; set; } = string.Empty;
    /// <summary>Model name</summary>
    public string? Model { get; set; }
    /// <summary>Tool group names</summary>
    public List<string>? ToolGroups { get; set; }
    /// <summary>Temperature parameter</summary>
    public double? Temperature { get; set; }
    /// <summary>Max tokens</summary>
    public int? MaxTokens { get; set; }
    /// <summary>Timeout in seconds</summary>
    public int? TimeoutSeconds { get; set; }
    /// <summary>Whether enabled</summary>
    public bool IsEnabled { get; set; }
    /// <summary>Execution mode</summary>
    public AgentExecutionMode ExecutionMode { get; set; }
    /// <summary>Typed execution configuration for multi-agent modes</summary>
    public AgentExecutionConfigDto? ExecutionConfig { get; set; }
    /// <summary>Domain tags (e.g. ["coding", "research", "writing"])</summary>
    public List<string>? Domains { get; set; }
    /// <summary>Role tags (e.g. ["reviewer", "implementer", "planner"])</summary>
    public List<string>? Roles { get; set; }
    /// <summary>Quality tier (1-5, 5=highest)</summary>
    public int QualityTier { get; set; }
    /// <summary>Latency tier (1-5, 1=fastest)</summary>
    public int LatencyTier { get; set; }
    /// <summary>Cost tier (1-5, 1=cheapest)</summary>
    public int CostTier { get; set; }
    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }
    /// <summary>Last modification time</summary>
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// 创建 Agent DTO
/// </summary>
public class CreateAgentDto
{
    /// <summary>Agent name</summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = null!;

    /// <summary>Agent description</summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>System instructions</summary>
    [MaxLength(4000)]
    public string? Instructions { get; set; }

    /// <summary>Provider name</summary>
    [Required]
    [MaxLength(50)]
    public string Provider { get; set; } = null!;

    /// <summary>Model name</summary>
    [MaxLength(100)]
    public string? Model { get; set; }

    /// <summary>Tool group names</summary>
    public List<string>? ToolGroups { get; set; }

    /// <summary>Temperature parameter</summary>
    public double? Temperature { get; set; }

    /// <summary>Max tokens</summary>
    public int? MaxTokens { get; set; }

    /// <summary>Timeout in seconds</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Whether enabled</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Execution mode</summary>
    public AgentExecutionMode ExecutionMode { get; set; } = AgentExecutionMode.Single;

    /// <summary>Typed execution configuration for multi-agent modes</summary>
    public AgentExecutionConfigDto? ExecutionConfig { get; set; }

    /// <summary>Domain tags (e.g. ["coding", "research", "writing"])</summary>
    public List<string>? Domains { get; set; }

    /// <summary>Role tags (e.g. ["reviewer", "implementer", "planner"])</summary>
    public List<string>? Roles { get; set; }

    /// <summary>Quality tier (1-5, 5=highest)</summary>
    [Range(1, 5)]
    public int QualityTier { get; set; } = 3;

    /// <summary>Latency tier (1-5, 1=fastest)</summary>
    [Range(1, 5)]
    public int LatencyTier { get; set; } = 3;

    /// <summary>Cost tier (1-5, 1=cheapest)</summary>
    [Range(1, 5)]
    public int CostTier { get; set; } = 3;
}

/// <summary>
/// 更新 Agent DTO
/// </summary>
public class UpdateAgentDto
{
    /// <summary>Agent name</summary>
    [MaxLength(200)]
    public string? Name { get; set; }

    /// <summary>Agent description</summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>System instructions</summary>
    [MaxLength(4000)]
    public string? Instructions { get; set; }

    /// <summary>Provider name</summary>
    [MaxLength(50)]
    public string? Provider { get; set; }

    /// <summary>Model name</summary>
    [MaxLength(100)]
    public string? Model { get; set; }

    /// <summary>Tool group names</summary>
    public List<string>? ToolGroups { get; set; }

    /// <summary>Temperature parameter</summary>
    public double? Temperature { get; set; }

    /// <summary>Max tokens</summary>
    public int? MaxTokens { get; set; }

    /// <summary>Timeout in seconds</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Whether enabled</summary>
    public bool? IsEnabled { get; set; }

    /// <summary>Execution mode</summary>
    public AgentExecutionMode? ExecutionMode { get; set; }

    /// <summary>Typed execution configuration for multi-agent modes</summary>
    public AgentExecutionConfigDto? ExecutionConfig { get; set; }

    /// <summary>Domain tags (e.g. ["coding", "research", "writing"])</summary>
    public List<string>? Domains { get; set; }

    /// <summary>Role tags (e.g. ["reviewer", "implementer", "planner"])</summary>
    public List<string>? Roles { get; set; }

    /// <summary>Quality tier (1-5, 5=highest)</summary>
    [Range(1, 5)]
    public int? QualityTier { get; set; }

    /// <summary>Latency tier (1-5, 1=fastest)</summary>
    [Range(1, 5)]
    public int? LatencyTier { get; set; }

    /// <summary>Cost tier (1-5, 1=cheapest)</summary>
    [Range(1, 5)]
    public int? CostTier { get; set; }

    /// <summary>Change note for version tracking</summary>
    [MaxLength(500)]
    public string? ChangeNote { get; set; }
}

/// <summary>
/// Agent 执行配置 DTO — 统一序列化/反序列化入口
/// </summary>
public class AgentExecutionConfigDto
{
    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Handoff 配置</summary>
    public HandoffExecutionConfigDto? Handoff { get; set; }

    /// <summary>Router 配置</summary>
    public RouterExecutionConfigDto? Router { get; set; }

    /// <summary>AgentAsTools 配置</summary>
    public AgentAsToolsExecutionConfigDto? AgentAsTools { get; set; }

    /// <summary>ExternalCli 模式配置</summary>
    public ExternalCliExecutionConfigDto? ExternalCli { get; set; }

    /// <summary>
    /// 序列化为 JSON 字符串（存入 Agent.Configuration）
    /// </summary>
    public static string? Serialize(AgentExecutionConfigDto? config)
    {
        if (config == null) return null;
        if (config.Handoff == null && config.Router == null && config.AgentAsTools == null && config.ExternalCli == null) return null;
        return JsonSerializer.Serialize(config, CamelCaseOptions);
    }

    /// <summary>
    /// 从 Agent.Configuration JSON 反序列化
    /// </summary>
    public static AgentExecutionConfigDto? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var config = JsonSerializer.Deserialize<AgentExecutionConfigDto>(json, CamelCaseOptions);
            if (config == null) return null;
            return config.Handoff == null && config.Router == null && config.AgentAsTools == null && config.ExternalCli == null
                ? null
                : config;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Handoff 执行配置 DTO
/// </summary>
public class HandoffExecutionConfigDto
{
    /// <summary>目标 Agent 名称到 AgentId 的映射</summary>
    public Dictionary<string, Guid> Targets { get; set; } = [];

    /// <summary>最大转接次数</summary>
    [Range(1, 20)]
    public int? MaxHandoffs { get; set; }

    /// <summary>是否允许目标 Agent 自动转回来源 Agent</summary>
    public bool? AllowReturnToSource { get; set; }
}

/// <summary>
/// Router 执行配置 DTO
/// </summary>
public class RouterExecutionConfigDto
{
    /// <summary>可路由的目标 Agent 名称到 AgentId 的映射</summary>
    public Dictionary<string, Guid> Targets { get; set; } = [];

    /// <summary>是否允许 Router 直接回答</summary>
    public bool? AllowDirectResponse { get; set; }
}

/// <summary>
/// AgentAsTools 执行配置 DTO
/// </summary>
public class AgentAsToolsExecutionConfigDto
{
    /// <summary>可调用的子 Agent 名称到 AgentId 的映射</summary>
    public Dictionary<string, Guid> Agents { get; set; } = [];
}

/// <summary>
/// ExternalCli 执行配置 DTO
/// </summary>
public class ExternalCliExecutionConfigDto
{
    /// <summary>CLI 提供者名称（对应 AI:Cli:Providers 的 key）</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>默认工作目录</summary>
    public string? WorkingDirectory { get; set; }
}

/// <summary>
/// Agent 列表查询 DTO
/// </summary>
public class AgentListQueryDto : PagedQueryDto
{
    protected override int DefaultPageSize => 20;

    /// <summary>Search keyword (name or description)</summary>
    public string? Keyword { get; set; }

    /// <summary>Filter by provider</summary>
    public string? Provider { get; set; }

    /// <summary>Filter by enabled status</summary>
    public bool? IsEnabled { get; set; }

    /// <summary>Filter by execution mode</summary>
    public AgentExecutionMode? ExecutionMode { get; set; }

    /// <summary>Filter by domain tag (e.g. "coding", "research") — matches if agent has this domain</summary>
    public string? Domain { get; set; }

    /// <summary>Filter by role tag (e.g. "reviewer", "planner") — matches if agent has this role</summary>
    public string? Role { get; set; }

    /// <summary>Minimum quality tier (1-5, 5=highest)</summary>
    [Range(1, 5)]
    public int? MinQualityTier { get; set; }

    /// <summary>Maximum latency tier (1-5, 1=fastest)</summary>
    [Range(1, 5)]
    public int? MaxLatencyTier { get; set; }

    /// <summary>Maximum cost tier (1-5, 1=cheapest)</summary>
    [Range(1, 5)]
    public int? MaxCostTier { get; set; }
}

/// <summary>
/// 运行 Agent 请求 DTO
/// </summary>
public class RunAgentRequestDto
{
    /// <summary>User message (text only, for simple requests)</summary>
    [MaxLength(10000)]
    public string? Message { get; set; }

    /// <summary>Multimodal content parts (text, image, file). Use this instead of Message for multimodal requests.</summary>
    public List<ContentPartDto>? Content { get; set; }

    /// <summary>Optional thread ID for conversation</summary>
    public Guid? ThreadId { get; set; }

    /// <summary>Optional user ID for quota check and usage update</summary>
    public Guid? UserId { get; set; }
}

/// <summary>
/// Agent 响应 DTO
/// </summary>
public class AgentResponseDto
{
    /// <summary>Response content</summary>
    public string Content { get; set; } = string.Empty;
    /// <summary>Finish reason</summary>
    public string? FinishReason { get; set; }
    /// <summary>Model used</summary>
    public string? Model { get; set; }
    /// <summary>Token usage</summary>
    public TokenUsageDto? Usage { get; set; }
    /// <summary>RAG citations (source documents used)</summary>
    public List<CitationDto>? Citations { get; set; }

    /// <summary>Execution path for multi-agent orchestration modes</summary>
    public List<string>? HandoffPath { get; set; }

    /// <summary>Final agent name that produced the answer</summary>
    public string? FinalAgentName { get; set; }
    /// <summary>Reasoning/thinking content (populated in non-streaming mode)</summary>
    public string? Reasoning { get; set; }
}

/// <summary>
/// Token 使用量 DTO
/// </summary>
/// <remarks>
/// 序列化格式: {"InputTokens":11649,"OutputTokens":20,"TotalTokens":11669,"CachedInputTokens":0,"CacheCreationTokens":0}
/// </remarks>
public class TokenUsageDto
{
    /// <summary>输入 Token 数（发送给模型的）</summary>
    public int InputTokens { get; set; }

    /// <summary>输出 Token 数（模型生成的）</summary>
    public int OutputTokens { get; set; }

    /// <summary>总 Token 数</summary>
    public int TotalTokens { get; set; }

    /// <summary>命中 Prompt Cache 的输入 Token 数（降低成本）</summary>
    public int CachedInputTokens { get; set; }

    /// <summary>首次写入 Prompt Cache 的 Token 数</summary>
    public int CacheCreationTokens { get; set; }
}

/// <summary>
/// Agent 版本快照 DTO
/// </summary>
public class AgentVersionDto
{
    /// <summary>Version record ID</summary>
    public Guid Id { get; set; }
    /// <summary>Agent ID</summary>
    public Guid AgentId { get; set; }
    /// <summary>Version number</summary>
    public int Version { get; set; }
    /// <summary>Change note</summary>
    public string? ChangeNote { get; set; }
    /// <summary>Config snapshot (JSON)</summary>
    public string ConfigSnapshot { get; set; } = string.Empty;
    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// Agent 版本列表查询 DTO
/// </summary>
public class AgentVersionQueryDto : PagedQueryDto
{
    protected override int DefaultPageSize => 20;
}
