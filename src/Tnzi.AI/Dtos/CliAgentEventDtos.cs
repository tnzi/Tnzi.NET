namespace Tnzi.AI.Dtos;

/// <summary>
/// 外部 agent 执行过程中产生的一个归一化事件。不可变。
/// </summary>
/// <remarks>
/// 这是**所有**协议适配器的共同出口：stream-json 的 <c>assistant</c> 内容块、
/// ACP 的 <c>session/update</c> 通知，都在适配器里塌缩成这一种形状。
/// 上层（持久化 / SSE / 前端时间线）因此完全不知道底下跑的是哪个 CLI。
/// </remarks>
public sealed record CliAgentEvent
{
    /// <summary>事件类型。</summary>
    public required CliAgentEventType Type { get; init; }

    /// <summary>文本内容（Text / Thinking / Error / Log）。</summary>
    public string? Content { get; init; }

    /// <summary>工具名（ToolUse / ToolResult）。</summary>
    public string? Tool { get; init; }

    /// <summary>工具调用 ID，用于把 ToolUse 与 ToolResult 配对。</summary>
    public string? CallId { get; init; }

    /// <summary>工具入参（ToolUse）。</summary>
    public IReadOnlyDictionary<string, object?>? Input { get; init; }

    /// <summary>工具输出（ToolResult）。</summary>
    public string? Output { get; init; }

    /// <summary>状态标识（Status），如 <c>running</c> / <c>rate_limit</c>。</summary>
    public string? Status { get; init; }

    /// <summary>日志级别（Log）。</summary>
    public string? Level { get; init; }

    /// <summary>
    /// 后端会话 ID。在 Status 事件上**尽早**出现，让调度层能在运行中途就固定 resume 指针 ——
    /// 进程中途崩溃时，等到终态才拿 sessionId 就已经晚了。
    /// </summary>
    public string? SessionId { get; init; }
}

/// <summary>
/// 单个模型的 token 用量。
/// </summary>
public sealed record CliAgentTokenUsage
{
    /// <summary>输入 token。</summary>
    public long InputTokens { get; init; }

    /// <summary>输出 token。</summary>
    public long OutputTokens { get; init; }

    /// <summary>缓存命中读取的 token。</summary>
    public long CacheReadTokens { get; init; }

    /// <summary>写入缓存的 token。</summary>
    public long CacheWriteTokens { get; init; }

    /// <summary>
    /// provider 自报成本（USD）。null = 未上报，回落到 <c>ICostCalculator</c> 估算。
    /// </summary>
    /// <remarks>
    /// 之所以优先用 provider 自报值：按 token 单价估算复现不了**请求级**定价规则
    /// （某些厂商在 prompt 超过阈值后整请求翻倍计价），而一条用量记录聚合了一个 turn 里
    /// 的多次模型调用，事后无法判断哪一次踩了哪一档。
    /// </remarks>
    public decimal? ReportedCostUsd { get; init; }
}

/// <summary>
/// 一次外部 agent 执行的终态。
/// </summary>
public sealed record CliAgentResult
{
    /// <summary>终态。</summary>
    public required CliRunStatus Status { get; init; }

    /// <summary>失败分类（<see cref="Status"/> 非成功时给出）。</summary>
    public CliRunFailureReason? FailureReason { get; init; }

    /// <summary>面向用户的最终交付物（不含过程叙述）。</summary>
    public string? Output { get; init; }

    /// <summary>完整文本流，供错误嗅探与审计。</summary>
    /// <remarks>
    /// 必须与 <see cref="Output"/> 并存：有些适配器把「重试 N 次后放弃」当普通消息发出，
    /// 而它可能落在最后一次工具调用**之前** —— 只看交付物会整段漏掉。
    /// </remarks>
    public string? FullTranscript { get; init; }

    /// <summary>错误信息（含 stderr 尾部）。</summary>
    public string? Error { get; init; }

    /// <summary>执行耗时（毫秒）。</summary>
    public long DurationMs { get; init; }

    /// <summary>provider 侧会话 ID，用于后续续接。</summary>
    public string? SessionId { get; init; }

    /// <summary>按模型名分组的 token 用量。</summary>
    public IReadOnlyDictionary<string, CliAgentTokenUsage> Usage { get; init; }
        = new Dictionary<string, CliAgentTokenUsage>();

    /// <summary>
    /// 正面证据：本次 resume <b>被拒绝</b>（会话不存在 / 属于其它账号 / 历史无法重放）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>false 不是反面证据。</b>对 <c>CliProviderDescriptor.ResumeRejectionDetectable == false</c>
    /// 的 provider 它表示「分不清」；对其余 provider 才表示「查过了，不是拒绝」。
    /// 调度层必须结合 provider 能力位一起读，否则会把「分不清」当成「不是拒绝」。
    /// </para>
    /// <para>
    /// 适配器<b>禁止</b>在以下失败上置位：网络中断、限流、配额、上游 5xx、认证错误。
    /// 那些必须保住会话指针，让平台的重试去续接被截断的对话；开新会话既治不好它们，
    /// 又白白丢掉整段上下文。
    /// </para>
    /// </remarks>
    public bool ResumeRejected { get; init; }
}
