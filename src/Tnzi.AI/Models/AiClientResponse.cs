namespace Tnzi.AI.Models;

/// <summary>
/// ITnziAiClient 非流式响应
/// </summary>
public class AiClientResponse
{
    /// <summary>响应文本</summary>
    public required string Text { get; init; }

    /// <summary>线程 ID</summary>
    public Guid? ThreadId { get; init; }

    /// <summary>Run ID（启用追踪时）</summary>
    public Guid? RunId { get; init; }

    /// <summary>Token 使用量</summary>
    public TokenUsageDto? Usage { get; init; }

    /// <summary>完成原因</summary>
    public string? FinishReason { get; init; }

    /// <summary>实际执行模型</summary>
    public string? Model { get; init; }

    /// <summary>实际执行提供商</summary>
    public string? Provider { get; init; }

    /// <summary>运行状态</summary>
    public AgentRunStatus? Status { get; init; }

    /// <summary>推理内容</summary>
    public string? Reasoning { get; init; }

    /// <summary>引用来源</summary>
    public List<CitationDto>? Citations { get; init; }

    /// <summary>执行路径</summary>
    public List<string>? HandoffPath { get; init; }

    /// <summary>最终回答 Agent 名称</summary>
    public string? FinalAgentName { get; init; }

    /// <summary>建议后续问题</summary>
    public List<string>? Suggestions { get; init; }

    /// <summary>本次运行产物</summary>
    public List<AgentArtifactDto>? Artifacts { get; init; }

    /// <summary>澄清问题</summary>
    public string? ClarificationQuestion { get; init; }
}
