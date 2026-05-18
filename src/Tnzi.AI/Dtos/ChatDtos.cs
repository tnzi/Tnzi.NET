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

    /// <summary>Per-request reasoning effort override (None = no reasoning)</summary>
    public ReasoningEffort? ReasoningEffort { get; set; }
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
    /// <summary>Handoff path (only set when handoff execution mode was used)</summary>
    public List<string>? HandoffPath { get; set; }
    /// <summary>RAG citations (source documents used)</summary>
    public List<CitationDto>? Citations { get; set; }
    /// <summary>Reasoning/thinking content (populated in non-streaming mode)</summary>
    public string? Reasoning { get; set; }
    /// <summary>Persisted user message ID for this turn (null when persistence was skipped).</summary>
    public Guid? UserMessageId { get; set; }
    /// <summary>Persisted assistant message ID for this turn (null when persistence was skipped).</summary>
    public Guid? AssistantMessageId { get; set; }
}

/// <summary>
/// RAG 引用来源
/// </summary>
public class CitationDto
{
    /// <summary>Source document name</summary>
    public string? SourceName { get; set; }
    /// <summary>Source document link</summary>
    public string? SourceLink { get; set; }
    /// <summary>Referenced text excerpt</summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>Relevance score</summary>
    public double? Score { get; set; }
}
