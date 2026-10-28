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

    /// <summary>帧的 uuid。assistant / user 帧的就是会话里那个对话条目的 id，截断续接时用它指位置。</summary>
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

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

    /// <summary>
    /// assistant 帧：这条消息包着一次 API 错误时的公开分类（<c>rate_limit</c>、<c>authentication_failed</c>……）。
    /// </summary>
    /// <remarks>
    /// 这几个错误字段一律按 <see cref="JsonElement"/> 收，读的时候再看类型：别的帧上同名字段可能是对象，
    /// 按字符串收一旦类型不符，整帧反序列化失败、被丢掉（丢的可能就是 result 帧）。
    /// </remarks>
    [JsonPropertyName("error")]
    public JsonElement? Error { get; set; }

    /// <summary>assistant 帧：API 错误的具体种类（<c>usage_limit_reached</c> = 账号自身的用量上限）。</summary>
    [JsonPropertyName("api_error")]
    public JsonElement? ApiError { get; set; }

    /// <summary>assistant 帧：<see cref="ApiError"/> 的参数；用量上限时带 <c>rate_limit_info</c>（哪个上限、何时重置）。</summary>
    [JsonPropertyName("api_error_params")]
    public JsonElement? ApiErrorParams { get; set; }

    /// <summary><c>rate_limit_event</c> 帧：配额窗口的状态。</summary>
    [JsonPropertyName("rate_limit_info")]
    public JsonElement? RateLimitInfo { get; set; }
}

/// <summary>
/// 一份 <c>rate_limit_info</c>：<c>rate_limit_event</c> 帧带的，或用量上限拒绝时 assistant 帧的 <c>api_error_params</c> 里带的，形状相同。
/// </summary>
/// <remarks>
/// 形状取自 claude 2.1.303 自带的消息 schema：<c>status</c> 是 <c>allowed</c> / <c>allowed_warning</c> / <c>rejected</c>，
/// <c>resetsAt</c> 是 Unix 秒，<c>rateLimitType</c> 是 <c>five_hour</c> / <c>seven_day</c> / <c>seven_day_opus</c> / ……
/// </remarks>
/// <param name="Status">窗口状态。</param>
/// <param name="ResetsAt">何时重置；没报时为 null。</param>
/// <param name="RateLimitType">哪一个上限；没报时为 null。</param>
internal sealed record StreamJsonRateLimit(string Status, DateTimeOffset? ResetsAt, string? RateLimitType)
{
    /// <summary>请求被这个上限拒绝了。</summary>
    public bool IsRejected => string.Equals(Status, "rejected", StringComparison.Ordinal);

    /// <summary>读一份 <c>rate_limit_info</c>；不是对象或没有 <c>status</c> 时为 null。</summary>
    public static StreamJsonRateLimit? Read(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } info
            || !info.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        DateTimeOffset? resetsAt = null;
        if (info.TryGetProperty("resetsAt", out var resets) && resets.ValueKind == JsonValueKind.Number
            && resets.TryGetInt64(out var seconds) && seconds > 0)
        {
            try
            {
                resetsAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                // 读不懂的时间等于没报：宁可说不出几点恢复，也不说一个错的
            }
        }

        var type = info.TryGetProperty("rateLimitType", out var kind) && kind.ValueKind == JsonValueKind.String
            ? kind.GetString()
            : null;

        return new StreamJsonRateLimit(status.GetString()!, resetsAt, type);
    }
}

/// <summary>assistant 帧里的消息体。</summary>
internal sealed class StreamJsonMessage
{
    /// <summary>
    /// API 回复的 id。claude 把一次回复的每个内容块拆成一帧，同一个回复的几帧带着同一个 id 与同一份 <see cref="Usage"/>，
    /// 按帧相加会把一次调用记成好几次。
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

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

    /// <summary>这次工具调用的 id（与 assistant 帧里的 tool_use 块同一个）。</summary>
    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>为什么要问（给人看的说明，可能带 ANSI 转义）。</summary>
    [JsonPropertyName("decision_reason")]
    public string? DecisionReason { get; set; }

    [JsonPropertyName("blocked_path")]
    public string? BlockedPath { get; set; }

    /// <summary>CLI 建议的「以后不再问」规则（<c>updatedPermissions</c> 的条目形状）。</summary>
    [JsonPropertyName("permission_suggestions")]
    public JsonElement? PermissionSuggestions { get; set; }
}
