namespace Tnzi.AI.Dtos;

/// <summary>
/// AI 运行结果
/// </summary>
public class AgentRunResult
{
    /// <summary>响应内容</summary>
    public required string Response { get; init; }

    /// <summary>关联的 Run ID（启用追踪时非 null）</summary>
    public Guid? RunId { get; init; }

    /// <summary>对话线程 ID</summary>
    public Guid? ThreadId { get; init; }

    /// <summary>Token 使用量</summary>
    public TokenUsageDto? Usage { get; init; }

    /// <summary>引用来源</summary>
    public List<CitationDto>? Citations { get; init; }

    /// <summary>完成原因</summary>
    public string? FinishReason { get; init; }

    /// <summary>执行路径（Handoff/Router 等多 Agent 模式）</summary>
    public List<string>? HandoffPath { get; init; }

    /// <summary>最终产生回答的 Agent 名称</summary>
    public string? FinalAgentName { get; init; }

    /// <summary>运行状态（启用追踪时非 null）</summary>
    public AgentRunStatus? Status { get; init; }

    /// <summary>推理/思考过程内容（非流式时填充）</summary>
    public string? Reasoning { get; init; }
}
