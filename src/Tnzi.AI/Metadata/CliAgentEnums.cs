namespace Tnzi.AI.Metadata;

/// <summary>
/// 外部 agent 执行过程中的归一化事件类型。
/// </summary>
/// <remarks>
/// 每种协议（stream-json / ACP / 厂商 app-server）的原生事件都由适配器映射到这几种上，
/// 上层（持久化、SSE、前端时间线）只认识这一套，不认识任何 provider 的私有形状。
/// </remarks>
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
    Log = 6
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
    ToolTimeout = 15
}

/// <summary>
/// 外部运行时的执行位置。
/// </summary>
/// <remarks>
/// 契约按远程 daemon 建模，首期只实现 <see cref="InProcess"/>。
/// 先立对契约、后补实现；反过来会推倒重来。
/// </remarks>
public enum CliRuntimeMode
{
    /// <summary>API 进程内起子进程。</summary>
    InProcess = 0,

    /// <summary>远程 daemon 认领并执行。</summary>
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
