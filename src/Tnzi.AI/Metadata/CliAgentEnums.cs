namespace Tnzi.AI.Metadata;

/// <summary>
/// 外部 agent 执行过程中的归一化事件类型。
/// </summary>
/// <remarks>
/// 每种协议（stream-json / ACP / 厂商 app-server）的原生事件都由适配器映射到这几种上，
/// 上层（持久化、SSE、前端时间线）只认识这一套，不认识任何 provider 的私有形状。
/// ★ 线上一律按名字写（<c>"Text"</c> / <c>"ToolUse"</c>），与框架其它 JSON 出口同口径：前端按名字判别，
/// 写成数字时流上的文本与时间线全丢（实时事件流曾按不带枚举转换器的选项序列化）。
/// 标在类型上，不论调用方传哪套序列化选项都一致；读取时数字仍然接受。
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<CliAgentEventType>))]
public enum CliAgentEventType
{
    /// <summary>面向用户的文本增量。</summary>
    Text = 0,

    /// <summary>推理/思考增量。</summary>
    Thinking = 1,

    /// <summary>工具调用发起。</summary>
    ToolUse = 2,

    /// <summary>工具调用结果。</summary>
    ToolResult = 3,

    /// <summary>状态播报（会话建立、限额窗口等），不是内容也不是错误。</summary>
    Status = 4,

    /// <summary>错误。</summary>
    Error = 5,

    /// <summary>运行时日志。未知事件类型一律降级到这里，不得崩溃。</summary>
    Log = 6,

    /// <summary>
    /// 一个等使用方回答的请求（<c>can_use_tool</c> / <c>session/request_permission</c>）：批准一个动作、回答 agent 的问题、批准 agent 的计划，
    /// 由 <see cref="Dtos.CliAgentPermission.Kind"/> 区分。只在这一轮有人值守时出现；<see cref="Dtos.CliAgentEvent.Permission"/> 带请求 id、种类、
    /// 选项与过期时间，问题与计划还带各自的内容。
    /// </summary>
    PermissionRequest = 7,

    /// <summary>
    /// 一个请求有了结局（允许、拒绝、超时、随运行结束）。<see cref="Dtos.CliAgentEvent.Permission"/> 的 <c>RequestId</c>
    /// 指向那条请求，<c>Status</c> 是结局。
    /// </summary>
    PermissionResolved = 8,

    /// <summary>
    /// 一轮对话在运行中途结束，而 CLI 还会接着跑下一轮（它还有后台任务在跑，任务完成时自己开一轮）。
    /// 这一轮的回答已经作为一条助手消息写进线程，<see cref="Dtos.CliAgentEvent.Turn"/> 带它的 id 与还在跑的后台任务；
    /// 之后的事件属于下一轮的回答。
    /// </summary>
    TurnCompleted = 9,

    /// <summary>
    /// CLI 压缩了会话上下文（手动的 <c>/compact</c> 或上下文将满时自动压缩）。<see cref="Dtos.CliAgentEvent.Compaction"/>
    /// 带触发方式与压缩前后的 token 数。
    /// </summary>
    Compacted = 10,

    /// <summary>
    /// 一个 CLI 任务开始、有了进展或结束了：agent 派出的子 agent（claude 的 <c>Agent</c> 工具），或在后台跑的命令。
    /// <see cref="Dtos.CliAgentEvent.CallId"/> 是起它的那次工具调用，<see cref="Dtos.CliAgentEvent.Task"/> 带种类、说明、状态与用量；
    /// 嵌套派出的子 agent 在 <see cref="Dtos.CliAgentEvent.ParentCallId"/> 上指向派出它的上一层。
    /// </summary>
    Task = 11,

    /// <summary>
    /// 线程消息队列交给这一轮的一条用户消息有了着落：agent 在两步之间收下了它（<see cref="Dtos.CliUserMessage.Taken"/>），
    /// 或者 CLI 不收它（拒绝、会话结束时还没轮到）。<see cref="Dtos.CliAgentEvent.UserMessage"/> 带排队消息的 id、正文，
    /// 收下时还带它写进线程的那条用户消息与它之前那一段回答。收下之后的事件属于对它的回答。
    /// </summary>
    UserMessage = 12
}

/// <summary>
/// 外部 agent 的一个权限请求处在什么状态。
/// </summary>
public enum CliPermissionStatus
{
    /// <summary>等使用方回答。</summary>
    Pending = 0,

    /// <summary>使用方允许（一次，或 CLI 支持时「本会话总是允许」）。</summary>
    Allowed = 1,

    /// <summary>使用方拒绝。</summary>
    Denied = 2,

    /// <summary>到了期限没人回答，按拒绝交给 CLI。</summary>
    TimedOut = 3,

    /// <summary>运行在回答之前结束（取消、失败、进程退出），按拒绝结束。</summary>
    Cancelled = 4
}

/// <summary>
/// 一次外部 agent 执行的状态。
/// </summary>
public enum CliRunStatus
{
    /// <summary>已入队，等待认领。</summary>
    Queued = 0,

    /// <summary>已被某个宿主认领（持租约），尚未真正起进程。</summary>
    Dispatched = 1,

    /// <summary>进程已启动，正在产出事件。</summary>
    Running = 2,

    /// <summary>正常结束。</summary>
    Completed = 3,

    /// <summary>失败结束。</summary>
    Failed = 4,

    /// <summary>被取消。</summary>
    Cancelled = 5,

    /// <summary>超时（看门狗触发）。</summary>
    TimedOut = 6
}

/// <summary>
/// 稳定的失败分类。
/// </summary>
/// <remarks>
/// 在**做出判断的那个分支**确定，不从错误字符串反推 —— 错误文案会随 CLI 升级漂移，
/// 反推出来的分类会静默变错。客户端按枚举本地化；枚举本身不泄漏私有资源是否存在。
/// </remarks>
public enum CliRunFailureReason
{
    /// <summary>未分类。</summary>
    Unknown = 0,

    /// <summary>PATH 上找不到可执行文件。</summary>
    ExecutableNotFound = 1,

    /// <summary>进程启动失败。</summary>
    LaunchFailed = 2,

    /// <summary>握手阶段超时（ACP initialize / 会话建立）。</summary>
    HandshakeTimeout = 3,

    /// <summary>上游模型 API 返回 4xx/5xx。</summary>
    ProviderError = 4,

    /// <summary>被限流。</summary>
    RateLimited = 5,

    /// <summary>配额耗尽。</summary>
    QuotaExceeded = 6,

    /// <summary>认证失败（token 过期等）。</summary>
    AuthenticationFailed = 7,

    /// <summary>网络错误。</summary>
    NetworkError = 8,

    /// <summary>空闲看门狗触发（完全没有事件）。</summary>
    IdleTimeout = 9,

    /// <summary>硬超时（挂钟总时长）触发。</summary>
    HardTimeout = 10,

    /// <summary>进程崩溃。</summary>
    ProcessCrashed = 11,

    /// <summary>续接会话被拒绝。</summary>
    ResumeRejected = 12,

    /// <summary>工作区布置失败。</summary>
    WorkspacePrepareFailed = 13,

    /// <summary>被取消。</summary>
    Cancelled = 14,

    /// <summary>工具看门狗触发（有 tool-use 未收到 tool-result）。</summary>
    ToolTimeout = 15,

    /// <summary>
    /// provider 账号自身的用量上限到了（订阅的会话 / 每周额度，或用量额度的上限）。
    /// 与 <see cref="QuotaExceeded"/>（框架自己的预算 / 配额门）不是一回事：这次是 CLI 背后的账号被拒，
    /// 到重置时间（运行记录的 <c>UsageLimitResetsAt</c>，知道时）之前再发也一样被拒。
    /// </summary>
    UsageLimit = 16,

    /// <summary>
    /// 框架给这个用户设的用量上限（5 小时窗口或每周窗口）到了：入队时还没到、排队期间被别的运行用到了上限，认领后拦下。
    /// 运行记录的 <c>UsageLimitResetsAt</c> 是那个窗口的重置时间。与 <see cref="UsageLimit"/>（CLI 背后账号的上限）不是一回事。
    /// </summary>
    UserUsageLimit = 17,

    /// <summary>
    /// 运行跑在一个能在服务端重启后重新接上的执行宿主上，但它的进程没能接回来：宿主报告进程已经不在（被杀掉、容器重启），
    /// 或者在 <c>AI:Cli:Reattach:Timeout</c> 之内一直接不上。已经写出的部分照常留在线程里，这一轮不重跑；
    /// 下一轮开新的 CLI 会话，并带上这一轮停在哪里的说明。<c>Error</c> 写着宿主给的原因。
    /// </summary>
    Interrupted = 18,

    /// <summary>
    /// 运行时所在的执行目标（例如某个用户的工作区容器）没有上线：使用方拒绝唤起它，
    /// 或者唤起之后在 <c>AI:Cli:Activation:Timeout</c> 之内一直没连上。进程一次都没起，<c>Error</c> 写着原因。
    /// </summary>
    TargetUnavailable = 19,

    /// <summary>
    /// 这一轮的输入 CLI 收不了：带着图片附件，而 CLI 在握手时报告它不收图片。整轮不跑，而不是只把文字发过去：
    /// 那样的回合看上去和成功一模一样，用户以为模型看到了图片。
    /// </summary>
    UnsupportedInput = 20
}

/// <summary>
/// 一个 provider 能随提示接收的附件种类。
/// </summary>
[Flags]
public enum CliAttachmentKinds
{
    /// <summary>不收附件。</summary>
    None = 0,

    /// <summary>PNG / JPEG / GIF / WebP 图片。</summary>
    Images = 1,

    /// <summary>PDF 文档。</summary>
    Documents = 2
}

/// <summary>
/// <see cref="CliAttachmentKinds"/> 的判断。
/// </summary>
public static class CliAttachmentKindsExtensions
{
    /// <summary>这些种类收不收某个媒体类型的附件（<c>image/*</c> 算图片，其余算文档）。</summary>
    public static bool Accepts(this CliAttachmentKinds kinds, string mediaType)
        => mediaType.StartsWith("image/", StringComparison.Ordinal)
            ? kinds.HasFlag(CliAttachmentKinds.Images)
            : kinds.HasFlag(CliAttachmentKinds.Documents);
}

/// <summary>
/// 外部运行时的执行位置。
/// </summary>
/// <remarks>
/// 决定哪个服务端副本认领这个运行时上的运行：<see cref="InProcess"/> 的运行由服务本机运行时的副本认领（任一副本都可以），
/// <see cref="RemoteDaemon"/> 的运行只由此刻连着那个执行目标的副本认领。
/// </remarks>
public enum CliRuntimeMode
{
    /// <summary>API 进程内起子进程。</summary>
    InProcess = 0,

    /// <summary>
    /// 登记在一个远程执行目标上（每用户一个的工作区容器、一台工作机）：<c>HostId</c> 是执行目标的标识，由使用方经
    /// <c>ICliRemoteRuntimeRegistry</c> 登记。
    /// </summary>
    RemoteDaemon = 1
}

/// <summary>
/// 外部运行时的可用状态。
/// </summary>
public enum CliRuntimeStatus
{
    /// <summary>不可达（心跳超期 / 未探测到）。</summary>
    Offline = 0,

    /// <summary>可用。</summary>
    Online = 1,

    /// <summary>被管理员停用。</summary>
    Disabled = 2
}

/// <summary>
/// 工作目录策略。
/// </summary>
public enum CliWorkDirectoryMode
{
    /// <summary>
    /// 每个<b>会话线程</b>一个目录（框架创建、框架回收）。<b>默认值。</b>
    /// </summary>
    /// <remarks>
    /// 这是唯一能让<b>多轮对话连续</b>的框架托管模式：编码 CLI 把会话按<b>项目目录</b>存档
    /// （claude 存在 <c>~/.claude/projects/&lt;cwd 哈希&gt;/</c>），所以只有同一个 cwd 反复出现，
    /// <c>--resume</c> 才找得到上一轮。顺带的好处：agent 自己写下的笔记与记忆文件也能跨轮留存。
    /// <para>
    /// 没有线程的调用（一次性任务）自动退化为按运行分目录 —— 那种运行本就没有下一轮。
    /// </para>
    /// </remarks>
    PerThread = 0,

    /// <summary>用户指定的绝对路径（框架**永不删除**，写入必须可精确回滚）。</summary>
    /// <remarks>
    /// 目录由用户拥有且跨轮稳定，所以会话续接<b>天然成立</b>，不需要框架做任何事。
    /// </remarks>
    UserProvided = 1,

    /// <summary>
    /// 每<b>一次运行</b>一个全新目录（框架创建、框架回收）。
    /// </summary>
    /// <remarks>
    /// <b>选了它就没有多轮连续性</b>，这是它存在的目的而不是副作用：
    /// 目录每轮重建，CLI 的会话存档和 agent 写下的任何文件都不会跨轮带过去。
    /// <para>
    /// 适用于每次执行必须从干净状态开始的批量任务（批量处理、评测、互不信任的租户任务）。
    /// 框架在这个模式下<b>不会尝试续接</b>：明知必被拒绝还发 <c>--resume</c>，
    /// 只会白白多跑一次重试并让用户付两次钱。
    /// </para>
    /// </remarks>
    PerRun = 2
}
