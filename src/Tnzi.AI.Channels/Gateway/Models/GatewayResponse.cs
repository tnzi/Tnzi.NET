using System.Text.Json.Serialization;

namespace Tnzi.AI.Channels.Gateway.Models;

/// <summary>
/// Gateway 响应 — 完整的非流式响应
/// </summary>
public class GatewayResponse
{
    /// <summary>是否成功</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>Agent 回复文本</summary>
    [JsonPropertyName("response")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Response { get; init; }

    /// <summary>关联的线程 ID</summary>
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ThreadId { get; init; }

    /// <summary>错误信息</summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
}

/// <summary>
/// Gateway 流式块 — 流式响应的单个数据块
/// </summary>
public class GatewayStreamChunk
{
    /// <summary>文本增量</summary>
    [JsonPropertyName("textDelta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TextDelta { get; init; }

    /// <summary>是否为最终块</summary>
    [JsonPropertyName("isFinal")]
    public bool IsFinal { get; init; }

    /// <summary>关联的线程 ID</summary>
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ThreadId { get; init; }

    /// <summary>完成原因（仅最终块）</summary>
    [JsonPropertyName("finishReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FinishReason { get; init; }
}
