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

    /// <summary>引用来源</summary>
    public List<CitationDto>? Citations { get; init; }
}
