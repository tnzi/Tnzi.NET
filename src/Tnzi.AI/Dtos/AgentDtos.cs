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
}

/// <summary>
/// Token 使用量 DTO
/// </summary>
public class TokenUsageDto
{
    /// <summary>Prompt tokens</summary>
    public int PromptTokens { get; set; }
    /// <summary>Completion tokens</summary>
    public int CompletionTokens { get; set; }
    /// <summary>Total tokens</summary>
    public int TotalTokens { get; set; }
}
