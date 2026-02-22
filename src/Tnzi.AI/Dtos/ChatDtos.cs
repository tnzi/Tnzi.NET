namespace Tnzi.AI.Dtos;

/// <summary>
/// 聊天请求 DTO
/// </summary>
public class ChatRequestDto
{
    /// <summary>User message (text only, for simple requests)</summary>
    [MaxLength(10000)]
    public string? Message { get; set; }

    /// <summary>Multimodal content parts (text, image, file). Use this instead of Message for multimodal requests.</summary>
    public List<ContentPartDto>? Content { get; set; }

    /// <summary>Optional agent ID</summary>
    public Guid? AgentId { get; set; }

    /// <summary>Optional thread ID for conversation</summary>
    public Guid? ThreadId { get; set; }

    /// <summary>Provider name</summary>
    [MaxLength(50)]
    public string? Provider { get; set; }

    /// <summary>Model name</summary>
    [MaxLength(100)]
    public string? Model { get; set; }

    /// <summary>Tool group names for this request (optional; used when no AgentId)</summary>
    public List<string>? ToolGroups { get; set; }

    /// <summary>User ID (for quota check)</summary>
    public Guid? UserId { get; set; }
}

/// <summary>
/// 聊天响应 DTO
/// </summary>
public class ChatResponseDto
{
    /// <summary>Response content</summary>
    public string Content { get; set; } = string.Empty;
    /// <summary>Finish reason</summary>
    public string? FinishReason { get; set; }
    /// <summary>Model used</summary>
    public string? Model { get; set; }
    /// <summary>Token usage</summary>
    public TokenUsageDto? Usage { get; set; }
    /// <summary>Thread ID</summary>
    public Guid? ThreadId { get; set; }
}
