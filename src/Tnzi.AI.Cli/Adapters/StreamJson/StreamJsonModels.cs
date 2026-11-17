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

    /// <summary>
    /// <c>system</c> 的 <c>init</c> / <c>status</c> 帧：会话此刻的权限档位（<c>plan</c>、<c>default</c>、<c>acceptEdits</c>……）。
    /// 实测 claude 2.1.317 每次换档都发一条 <c>status</c>：进计划（<c>EnterPlanMode</c> 或启动即计划档）、计划获批后回到原档位。
    /// </summary>
    [JsonPropertyName("permissionMode")]
    public string? PermissionMode { get; set; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    /// <summary>帧的 uuid。assistant / user 帧的就是会话里那个对话条目的 id，截断续接时用它指位置。</summary>
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("message")]
    public JsonElement? Message { get; set; }

    /// <summary>
    /// assistant / user 帧：这条消息属于哪个工具调用里的子 agent（<c>Task</c>）；主对话的消息为 null。
    /// 上下文窗口的占用只看主对话：子 agent 有它自己的上下文。
    /// </summary>
    [JsonPropertyName("parent_tool_use_id")]
    public string? ParentToolUseId { get; set; }

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

    /// <summary><c>control_response</c> 帧：CLI 对框架发出的控制请求的应答（<c>subtype</c> <c>success</c> / <c>error</c>、<c>request_id</c>、<c>error</c>）。</summary>
    [JsonPropertyName("response")]
    public JsonElement? Response { get; set; }

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

    /// <summary><c>system/background_tasks_changed</c> 帧：此刻还在跑的后台任务（整份，不是增量）。</summary>
    [JsonPropertyName("tasks")]
    public JsonElement? Tasks { get; set; }

    /// <summary><c>system/task_notification</c> 帧：哪个任务。</summary>
    [JsonPropertyName("task_id")]
    public string? TaskId { get; set; }

    /// <summary><c>system/task_notification</c> 帧：CLI 写的一句结果（"Background command "…" completed (exit code 0)"）。</summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    // 任务帧的字段。description / prompt / is_backgrounded / status 这几个常见的名字按元素收、读时再看类型：
    // 同名字段在别的帧上可能是别的类型，类型不符会让整帧反序列化失败、被丢掉。

    /// <summary>
    /// <c>system/task_started</c> / <c>task_progress</c> / <c>task_notification</c> 帧：派出这个任务的那次工具调用。子 agent 的帧上
    /// <see cref="ParentToolUseId"/> 就是它。
    /// </summary>
    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; set; }

    /// <summary><c>system/task_started</c> 帧：任务的种类（<c>local_agent</c> = 子 agent，<c>local_bash</c> = 后台命令……）。</summary>
    [JsonPropertyName("task_type")]
    public string? TaskType { get; set; }

    /// <summary><c>system/task_started</c> / <c>task_progress</c> 帧：子 agent 的类型（<c>general-purpose</c>、<c>Explore</c>……）。</summary>
    [JsonPropertyName("subagent_type")]
    public string? SubagentType { get; set; }

    /// <summary><c>system/task_started</c> 帧：任务的简短说明；<c>task_progress</c> 帧：它此刻在做什么。</summary>
    [JsonPropertyName("description")]
    public JsonElement? Description { get; set; }

    /// <summary><c>system/task_started</c> 帧：交给子 agent 的任务。</summary>
    [JsonPropertyName("prompt")]
    public JsonElement? Prompt { get; set; }

    /// <summary><c>system/task_started</c> 帧：是不是在后台跑。</summary>
    [JsonPropertyName("is_backgrounded")]
    public JsonElement? IsBackgrounded { get; set; }

    /// <summary><c>system/task_started</c> 帧：嵌套派出时上一层子 agent 的任务 id。</summary>
    [JsonPropertyName("parent_task_id")]
    public string? ParentTaskId { get; set; }

    /// <summary><c>system/task_progress</c> 帧：子 agent 最近调的工具。</summary>
    [JsonPropertyName("last_tool_name")]
    public string? LastToolName { get; set; }

    /// <summary><c>system/task_notification</c> 帧：任务的结局（<c>completed</c> / <c>failed</c> / <c>stopped</c>）。</summary>
    [JsonPropertyName("status")]
    public JsonElement? Status { get; set; }

    /// <summary><c>system/compact_boundary</c> 帧：触发方式与压缩前后的 token 数。</summary>
    [JsonPropertyName("compact_metadata")]
    public JsonElement? CompactMetadata { get; set; }

    /// <summary>result 帧：这一轮是 CLI 自己处理的本地命令（<c>compact</c> 之类）时它的名字。按元素收，读时再看类型。</summary>
    [JsonPropertyName("local_command")]
    public JsonElement? LocalCommand { get; set; }

    /// <summary>result 帧：CLI 排着队、还没开始的轮次数（2.1.316 起有）。</summary>
    [JsonPropertyName("queued_turn_count")]
    public int? QueuedTurnCount { get; set; }

    /// <summary>
    /// result 帧：这一轮是谁开的（<c>{"kind":"task-notification", ...}</c> = 一条任务通知）；人发的那一轮没有它。按元素收，读时再看类型。
    /// </summary>
    [JsonPropertyName("origin")]
    public JsonElement? Origin { get; set; }

    /// <summary>
    /// result 帧：这一轮回答了哪些带 uuid 的用户消息（开出它的那条，加上一轮进行中收下的），按收下的先后。按元素收，读时再看类型。
    /// </summary>
    [JsonPropertyName("user_message_uuids")]
    public JsonElement? UserMessageUuids { get; set; }

    /// <summary><c>command_lifecycle</c> 帧：带 uuid 的那条用户消息（我们写进 stdin 时给的 uuid）。</summary>
    [JsonPropertyName("command_uuid")]
    public string? CommandUuid { get; set; }

    /// <summary>
    /// <c>command_lifecycle</c> 帧：它此刻的处境：<c>queued</c>（进了 CLI 的队列）、<c>started</c>（并进一轮，agent 收下了）、
    /// <c>completed</c>、<c>cancelled</c>、<c>discarded</c>、<c>refused</c>。
    /// </summary>
    [JsonPropertyName("state")]
    public string? LifecycleState { get; set; }
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

    /// <summary><c>system/task_progress</c> / <c>task_notification</c> 帧：子 agent 到目前为止用的 token 合计。</summary>
    [JsonPropertyName("total_tokens")]
    public long? TotalTokens { get; set; }

    /// <summary><c>system/task_progress</c> / <c>task_notification</c> 帧：子 agent 到目前为止调了几次工具。</summary>
    [JsonPropertyName("tool_uses")]
    public int? ToolUses { get; set; }

    /// <summary><c>system/task_progress</c> / <c>task_notification</c> 帧：子 agent 到目前为止跑了多久（毫秒）。</summary>
    [JsonPropertyName("duration_ms")]
    public long? DurationMs { get; set; }
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

    /// <summary>这个模型的上下文窗口大小（2.1.316 实测有，如 <c>1000000</c>）；没报时为 null。</summary>
    [JsonPropertyName("contextWindow")]
    public long? ContextWindow { get; set; }
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
