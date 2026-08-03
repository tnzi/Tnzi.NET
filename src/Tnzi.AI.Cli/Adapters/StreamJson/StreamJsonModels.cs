// R3：二级目录（Adapters/StreamJson/）只是开发期分类，不产生子命名空间。
namespace Tnzi.AI.Cli.Adapters;

/// <summary>
/// stream-json 协议的一帧。
/// </summary>
/// <remarks>
/// 只声明框架实际消费的字段。未声明的字段被忽略而不是报错 —— CLI 会随版本增加新字段，
/// 严格反序列化会让每次上游小版本升级都变成一次生产故障。
/// </remarks>
internal sealed class StreamJsonFrame
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("subtype")]
    public string? Subtype { get; set; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("message")]
    public JsonElement? Message { get; set; }

    [JsonPropertyName("result")]
    public string? ResultText { get; set; }

    [JsonPropertyName("is_error")]
    public bool IsError { get; set; }

    [JsonPropertyName("num_turns")]
    public int NumTurns { get; set; }

    [JsonPropertyName("total_cost_usd")]
    public decimal TotalCostUsd { get; set; }

    [JsonPropertyName("duration_ms")]
    public double DurationMs { get; set; }

    [JsonPropertyName("usage")]
    public StreamJsonUsage? Usage { get; set; }

    [JsonPropertyName("modelUsage")]
    public Dictionary<string, StreamJsonModelUsage>? ModelUsage { get; set; }

    [JsonPropertyName("log")]
    public StreamJsonLog? Log { get; set; }

    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    [JsonPropertyName("request")]
    public JsonElement? Request { get; set; }
}

/// <summary>assistant 帧里的消息体。</summary>
internal sealed class StreamJsonMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("content")]
    public List<StreamJsonContentBlock>? Content { get; set; }

    [JsonPropertyName("usage")]
    public StreamJsonUsage? Usage { get; set; }
}

/// <summary>一个内容块（text / thinking / tool_use）。</summary>
internal sealed class StreamJsonContentBlock
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>
    /// thinking 块的正文。不同 CLI 版本有的放这里、有的放 <see cref="Text"/>，
    /// 两个都读，取先非空的那个。
    /// </summary>
    [JsonPropertyName("thinking")]
    public string? Thinking { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("input")]
    public JsonElement? Input { get; set; }

    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; set; }

    [JsonPropertyName("content")]
    public JsonElement? Content { get; set; }

    [JsonPropertyName("is_error")]
    public bool IsError { get; set; }
}

/// <summary>token 用量（snake_case 形态，出现在 message.usage）。</summary>
internal sealed class StreamJsonUsage
{
    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; set; }

    [JsonPropertyName("cache_read_input_tokens")]
    public long CacheReadInputTokens { get; set; }

    [JsonPropertyName("cache_creation_input_tokens")]
    public long CacheCreationInputTokens { get; set; }
}

/// <summary>token 用量（camelCase 形态，出现在 result.modelUsage）。</summary>
internal sealed class StreamJsonModelUsage
{
    [JsonPropertyName("inputTokens")]
    public long InputTokens { get; set; }

    [JsonPropertyName("outputTokens")]
    public long OutputTokens { get; set; }

    [JsonPropertyName("cacheReadInputTokens")]
    public long CacheReadInputTokens { get; set; }

    [JsonPropertyName("cacheCreationInputTokens")]
    public long CacheCreationInputTokens { get; set; }

    [JsonPropertyName("costUSD")]
    public decimal? CostUsd { get; set; }
}

/// <summary>log 帧的负载。</summary>
internal sealed class StreamJsonLog
{
    [JsonPropertyName("level")]
    public string? Level { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>control_request 帧的负载。</summary>
internal sealed class StreamJsonControlRequest
{
    [JsonPropertyName("subtype")]
    public string? Subtype { get; set; }

    [JsonPropertyName("tool_name")]
    public string? ToolName { get; set; }

    [JsonPropertyName("input")]
    public JsonElement? Input { get; set; }
}
