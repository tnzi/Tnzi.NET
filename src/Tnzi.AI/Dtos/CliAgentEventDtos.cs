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

    /// <summary>工具输出（ToolResult）的文本形态：文本块按顺序拼接，图片块写成一行占位说明。</summary>
    public string? Output { get; init; }

    /// <summary>
    /// 工具输出（ToolResult）的原样分块：文本与图片，按出现顺序。
    /// </summary>
    /// <remarks>
    /// 适配器给出的图片块带 base64 的 <see cref="CliContentBlock.Data"/>；执行器把图片存成附件、换成
    /// <see cref="CliContentBlock.AttachmentId"/> 之后才落库，所以从事件流读回来的图片块只有 id 没有数据。
    /// 不认识分块的适配器（ACP）留空，只给 <see cref="Output"/>。
    /// </remarks>
    public IReadOnlyList<CliContentBlock>? OutputBlocks { get; init; }

    /// <summary>状态标识（Status），如 <c>running</c> / <c>rate_limit</c>。</summary>
    public string? Status { get; init; }

    /// <summary>日志级别（Log）。</summary>
    public string? Level { get; init; }

    /// <summary>
    /// 后端会话 ID。适配器一拿到就放在事件上带出来，执行器记下流里最后看到的那个，
    /// 运行到达终态时与结果一起存成下一轮的续接指针。运行中途不落库：进程崩溃、运行被重新执行时，
    /// 沿用的是第一次执行开始时定下的指针。
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// 事件在本次运行里的序号（落库时分配）。从事件流读出的事件才有；断线重连、刷新后重新接上时，
    /// 客户端把收到的最后一个序号带回来，从它之后接着收。
    /// </summary>
    public int? Sequence { get; init; }

    /// <summary>事件落库的时间（从事件流读出的事件才有）。</summary>
    public DateTime? CreatedAt { get; init; }

    /// <summary>
    /// 权限请求与它的结局（<see cref="CliAgentEventType.PermissionRequest"/> / <see cref="CliAgentEventType.PermissionResolved"/>）。
    /// </summary>
    /// <remarks>
    /// 工具名在 <see cref="Tool"/>、工具调用 id 在 <see cref="CallId"/>（与那次调用的 ToolUse 事件同一个，界面据此把批准卡片挂到工具卡片上）、
    /// 入参在 <see cref="Input"/>、CLI 给的说明在 <see cref="Content"/>。
    /// </remarks>
    public CliAgentPermission? Permission { get; init; }
}

/// <summary>
/// 一个权限请求：CLI 要做一件档位不自动放行的事，等使用方回答。
/// </summary>
/// <remarks>
/// 请求事件带 <see cref="RequestId"/>、<see cref="Options"/> 与 <see cref="ExpiresAt"/>；结局事件带同一个 <see cref="RequestId"/>、
/// <see cref="Status"/> 与选中的 <see cref="OptionId"/>。回答经 <c>POST .../cli-runs/{runId}/permissions/{requestId}</c>
/// 或聊天的 <c>POST chat/runs/{runId}/permissions/{requestId}</c>，从 <see cref="Options"/> 里挑一个。
/// </remarks>
public sealed record CliAgentPermission
{
    /// <summary>框架给这个请求的 id：回答接口用它。</summary>
    public Guid? RequestId { get; init; }

    /// <summary>
    /// 适配器认这个请求用的键（CLI 的请求 id），只在适配器与执行器之间传递，不落库也不出现在线上。
    /// </summary>
    [JsonIgnore]
    public string? ProtocolKey { get; init; }

    /// <summary>CLI 给的标题（如 claude 的 <c>title</c> / <c>display_name</c>、ACP 的 <c>toolCall.title</c>）。</summary>
    public string? Title { get; init; }

    /// <summary>CLI 说的为什么要问（claude 的 <c>decision_reason</c>），没说时为 null。</summary>
    public string? Reason { get; init; }

    /// <summary>触发这次请求的路径（claude 的 <c>blocked_path</c>：工作目录之外的文件等），没有时为 null。</summary>
    public string? BlockedPath { get; init; }

    /// <summary>可选的回答。至少有一个允许项与一个拒绝项。</summary>
    public IReadOnlyList<CliPermissionOption> Options { get; init; } = [];

    /// <summary>过了这个时刻（UTC）没人回答就按拒绝交给 CLI。</summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>请求事件上恒为 <see cref="CliPermissionStatus.Pending"/>；结局事件上是结局。</summary>
    public CliPermissionStatus Status { get; init; } = CliPermissionStatus.Pending;

    /// <summary>结局事件：交给 CLI 的选项 id（超时、随运行结束时是框架代选的拒绝项，没有可选的拒绝项时为 null）。</summary>
    public string? OptionId { get; init; }

    /// <summary>结局事件：回答的人；超时与随运行结束时为 null。</summary>
    public Guid? AnsweredBy { get; init; }
}

/// <summary>
/// 权限请求的一个可选回答。
/// </summary>
/// <param name="Id">选项 id，回答时原样带回。</param>
/// <param name="Name">给人看的名字（CLI 给的，或框架按种类起的英文名）。</param>
/// <param name="Kind">种类：<see cref="CliPermissionOptionKinds"/> 之一（CLI 给出别的种类时原样保留，按拒绝对待）。</param>
public sealed record CliPermissionOption(string Id, string Name, string Kind);

/// <summary>
/// 权限选项的种类（与 ACP 的 <c>PermissionOptionKind</c> 同一套词）。
/// </summary>
public static class CliPermissionOptionKinds
{
    /// <summary>只允许这一次。</summary>
    public const string AllowOnce = "allow_once";

    /// <summary>允许，并且以后同类请求不再问（claude：本会话内；ACP：由 agent 决定范围）。</summary>
    public const string AllowAlways = "allow_always";

    /// <summary>只拒绝这一次。</summary>
    public const string RejectOnce = "reject_once";

    /// <summary>拒绝，并且以后同类请求一律拒绝（由 agent 决定范围）。</summary>
    public const string RejectAlways = "reject_always";

    /// <summary>这个种类是不是允许。认不出的种类一律不是。</summary>
    public static bool IsAllow(string? kind)
        => string.Equals(kind, AllowOnce, StringComparison.OrdinalIgnoreCase)
           || string.Equals(kind, AllowAlways, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 工具输出里的一个块。
/// </summary>
public sealed record CliContentBlock
{
    /// <summary>文本块。</summary>
    public const string TextType = "text";

    /// <summary>图片块。</summary>
    public const string ImageType = "image";

    /// <summary>
    /// 文件块：工具交给用户的一个文件（MCP 的 <c>resource_link</c>），只有名字与地址，不带内容。
    /// </summary>
    public const string FileType = "file";

    /// <summary>块类型：<see cref="TextType"/>、<see cref="ImageType"/> 或 <see cref="FileType"/>。</summary>
    public required string Type { get; init; }

    /// <summary>文本块的内容。</summary>
    public string? Text { get; init; }

    /// <summary>图片块的媒体类型。</summary>
    public string? MediaType { get; init; }

    /// <summary>图片块的 base64 数据：只在适配器到执行器之间存在，落库前换成 <see cref="AttachmentId"/>。</summary>
    public string? Data { get; init; }

    /// <summary>存档后的图片附件 id。</summary>
    public Guid? AttachmentId { get; init; }

    /// <summary>文件块的名字（工具给的，例如 <c>report.pdf</c>）。</summary>
    public string? Name { get; init; }

    /// <summary>文件块的地址：工具给的 URI，原样保留，由消费方决定它在界面上链接到哪里。</summary>
    public string? Uri { get; init; }
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

    /// <summary>
    /// 本次运行在 provider 会话里留下的最后一个条目的 id（claude：流里最后一个 assistant / user 帧的 uuid）。
    /// </summary>
    /// <remarks>
    /// 以后要丢掉这次运行之后的那些轮次（编辑、重新生成）时，从这个条目处截断续接（<c>--resume-session-at</c>）。
    /// 不支持截断的协议留空。
    /// </remarks>
    public string? SessionEntryId { get; init; }

    /// <summary>
    /// <see cref="FailureReason"/> 为 <see cref="CliRunFailureReason.UsageLimit"/> 时，CLI 报出的重置时间；不知道时为 null。
    /// </summary>
    public DateTimeOffset? UsageLimitResetsAt { get; init; }

    /// <summary>按模型名分组的 token 用量。</summary>
    /// <remarks>
    /// 适配器给出的是协议报出来的原样数字；<see cref="UsageIsSessionTotal"/> 为 true 时它是会话累计值，
    /// 执行器据同一会话上一轮记下的累计值折算出本轮，运行记录与配额只认折算后的那份。
    /// </remarks>
    public IReadOnlyDictionary<string, CliAgentTokenUsage> Usage { get; init; }
        = new Dictionary<string, CliAgentTokenUsage>();

    /// <summary>
    /// true = <see cref="Usage"/> 是 CLI 报的会话累计值，不是这一轮的。
    /// </summary>
    /// <remarks>
    /// claude 的 <c>result.modelUsage</c> 就是这样：续接（含截断续接分叉出的新会话）的进程从会话自己保存的总量接着算，
    /// 第一帧 result 就已经带着之前每一轮。没有续接时它等于这一轮。
    /// </remarks>
    public bool UsageIsSessionTotal { get; init; }

    /// <summary>
    /// CLI 自己数的这一轮主循环用量（不分模型），没报时为 null。
    /// </summary>
    /// <remarks>
    /// 只用来判断会话累计值里有没有带上之前的轮次：带上了，「累计值减去上一轮的累计值」至少要装得下它。
    /// 它不含工具内部调用的模型（WebFetch 里的小模型之类），所以不能直接当这一轮的用量。
    /// </remarks>
    public CliAgentTokenUsage? TurnUsage { get; init; }

    /// <summary>
    /// 正面证据：本次 resume <b>被拒绝</b>（会话不存在 / 属于其它账号 / 历史无法重放）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>false 不是反面证据。</b>对 <c>CliProviderDescriptor.ResumeRejectionDetectable == false</c>
    /// 的 provider 它表示「分不清」；对其余 provider 才表示「查过了，不是拒绝」。
    /// 能力位由适配器读：靠短语与佐证推断的那一种（stream-json）只对声明了它的 provider 推断，
    /// 所以 true 一律是正面证据，调度层见 true 才清掉会话指针并开新会话重试，见 false 从不据此推断任何事。
    /// </para>
    /// <para>
    /// 适配器<b>禁止</b>在以下失败上置位：网络中断、限流、配额、上游 5xx、认证错误。
    /// 那些必须保住会话指针，让平台的重试去续接被截断的对话；开新会话既治不好它们，
    /// 又白白丢掉整段上下文。
    /// </para>
    /// </remarks>
    public bool ResumeRejected { get; init; }
}
