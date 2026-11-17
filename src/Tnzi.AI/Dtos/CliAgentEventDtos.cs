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

    /// <summary>一轮在运行中途结束（<see cref="CliAgentEventType.TurnCompleted"/>）：写进线程的那条回答与还在跑的后台任务。</summary>
    public CliTurnBoundary? Turn { get; init; }

    /// <summary>一次上下文压缩（<see cref="CliAgentEventType.Compacted"/>）。</summary>
    public CliCompaction? Compaction { get; init; }

    /// <summary>
    /// 这条事件发生在哪个子 agent 里：派出它的那次工具调用的 <see cref="CallId"/>（claude 的 <c>parent_tool_use_id</c>）。
    /// 主 agent 自己的事件为 null。
    /// </summary>
    /// <remarks>
    /// 文本、思考、工具调用与结果都可能带它：子 agent 写的字不是给用户的回答，它调的工具也不是主 agent 调的，
    /// 上层据此把它们归到派出它的那张卡片下，而不是混进回复。<see cref="CliAgentEventType.Task"/> 事件上它指向
    /// 派出这个子 agent 的上一层子 agent（嵌套派出时），第一层为 null。没有子 agent 概念的协议（ACP）恒为 null。
    /// </remarks>
    public string? ParentCallId { get; init; }

    /// <summary>一个 CLI 任务（子 agent、后台命令）的开始、进展或结束（<see cref="CliAgentEventType.Task"/>）。</summary>
    public CliTask? Task { get; init; }

    /// <summary>线程消息队列交给这一轮的一条用户消息有了着落（<see cref="CliAgentEventType.UserMessage"/>）。</summary>
    public CliUserMessage? UserMessage { get; init; }
}

/// <summary>
/// 一轮进行中交给 CLI 的一条用户消息有了着落（<see cref="CliAgentEventType.UserMessage"/> 事件的载荷）。
/// </summary>
/// <remarks>
/// 收下时，它之前的那一段回答已经作为一条助手消息写进线程（<see cref="AssistantMessageId"/>），它自己作为一条用户消息写在后面
/// （<see cref="ThreadMessageId"/>），之后的回答写成另一条：刷新之后对话按「回答 → 用户插话 → 接着的回答」的次序读。
/// </remarks>
public sealed record CliUserMessage
{
    /// <summary>排队消息的 id（也是交给 CLI 时这条用户消息的 uuid）。</summary>
    public Guid MessageId { get; init; }

    /// <summary>true = agent 收下了；false = CLI 不收（<see cref="Reason"/> 说为什么），它回到队列里扣住。</summary>
    public bool Taken { get; init; }

    /// <summary>正文。</summary>
    public string? Text { get; init; }

    /// <summary>CLI 不收时的说法；收下时为 null。</summary>
    public string? Reason { get; init; }

    /// <summary>它写进线程的那条用户消息；没写（没有线程、没收下）时为 null。</summary>
    public Guid? ThreadMessageId { get; init; }

    /// <summary>收下它之前那一段回答写成的助手消息；那一段什么都没说时为 null。</summary>
    public Guid? AssistantMessageId { get; init; }
}

/// <summary>
/// 一个 CLI 任务此刻的样子（<see cref="CliAgentEventType.Task"/> 事件的载荷）：agent 派出的子 agent，或在后台跑的命令。
/// </summary>
/// <remarks>
/// 开始时带种类、说明与（子 agent 的）类型和任务；进展时带用到的工具数、token 与用时，以及它正在做什么；结束时带结局与报告。
/// 没报的字段为 null（<see cref="Status"/> 也一样：null = 状态没变），上层按「有值的覆盖」合并同一个任务的几条事件。
/// </remarks>
public sealed record CliTask
{
    /// <summary>CLI 给这个任务的 id（claude 的 <c>task_id</c>；停下它要用这个 id）。</summary>
    public string? TaskId { get; init; }

    /// <summary>种类：<see cref="CliTaskKinds"/> 之一，认不出的原样转交 CLI 的说法（claude 的 <c>task_type</c>）。</summary>
    public string? Kind { get; init; }

    /// <summary>子 agent 的类型（claude 的 <c>subagent_type</c>，如 <c>general-purpose</c>、<c>Explore</c>）。</summary>
    public string? AgentType { get; init; }

    /// <summary>子 agent 用的模型（从它的回复帧上读到的）。嵌套的子 agent 的帧 CLI 不发，读不到。</summary>
    public string? Model { get; init; }

    /// <summary>派它时给的简短说明（几个词）。</summary>
    public string? Description { get; init; }

    /// <summary>交给子 agent 的完整任务。</summary>
    public string? Prompt { get; init; }

    /// <summary>true = 在后台跑（派出它的那次工具调用当场返回，完成时 CLI 收到通知）。</summary>
    public bool? Background { get; init; }

    /// <summary>状态：<see cref="CliTaskStatuses"/> 之一；null = 这条事件没说状态（只带了别的字段）。</summary>
    public string? Status { get; init; }

    /// <summary>它到目前为止调了几次工具。</summary>
    public int? ToolUseCount { get; init; }

    /// <summary>它到目前为止用的 token（CLI 报的合计）。</summary>
    public long? TotalTokens { get; init; }

    /// <summary>它到目前为止跑了多久（毫秒）。</summary>
    public long? DurationMs { get; init; }

    /// <summary>它正在做什么（claude 每次进展给的一句话，如 "Reading alpha.txt"）。</summary>
    public string? Activity { get; init; }

    /// <summary>它最近调的工具名。</summary>
    public string? LastTool { get; init; }

    /// <summary>结束时的报告（claude <c>task_notification</c> 的 <c>summary</c>：子 agent 交回的报告、后台命令的退出说明）。</summary>
    public string? Summary { get; init; }
}

/// <summary>
/// CLI 任务的种类（<see cref="CliTask.Kind"/>）。
/// </summary>
public static class CliTaskKinds
{
    /// <summary>agent 派出的子 agent（claude <c>task_type: local_agent</c>）。</summary>
    public const string Agent = "agent";

    /// <summary>一条命令（claude <c>task_type: local_bash</c>，通常是 <c>run_in_background</c> 起的）。</summary>
    public const string Command = "command";

    /// <summary>CLI 的任务种类 → 框架的说法；认不出的原样返回。</summary>
    public static string? FromClaude(string? taskType) => taskType switch
    {
        "local_agent" => Agent,
        "local_bash" => Command,
        _ => taskType
    };
}

/// <summary>
/// CLI 任务的状态（<see cref="CliTask.Status"/>）。
/// </summary>
public static class CliTaskStatuses
{
    /// <summary>还在跑。</summary>
    public const string Running = "running";

    /// <summary>做完了。</summary>
    public const string Completed = "completed";

    /// <summary>出错结束。</summary>
    public const string Failed = "failed";

    /// <summary>被停掉（agent 用 <c>TaskStop</c> 停的、用户停下它或这次运行、运行结束时它还没做完）。</summary>
    public const string Stopped = "stopped";

    /// <summary>这个状态是不是一个结局。</summary>
    public static bool IsFinished(string? status) => status is Completed or Failed or Stopped;
}

/// <summary>
/// 运行中途结束的一轮：CLI 还有后台任务在跑，任务完成时它自己开下一轮。
/// </summary>
/// <remarks>
/// 运行在这之后保持 Running，直到 CLI 不再有后台任务、或等待到了 <c>AI:Cli:BackgroundTaskWait</c> 的上限。
/// 每一轮的回答是线程里单独的一条助手消息；界面在这个事件处收起当前的回答，之后的文本开一条新的。
/// </remarks>
public sealed record CliTurnBoundary
{
    /// <summary>这一轮写进线程的助手消息；没写（没有线程、这一轮什么都没说）时为 null。</summary>
    public Guid? AssistantMessageId { get; init; }

    /// <summary>这一轮结束时还在跑的后台任务。</summary>
    public IReadOnlyList<CliBackgroundTask> BackgroundTasks { get; init; } = [];
}

/// <summary>CLI 的一个后台任务（如以 <c>run_in_background</c> 起的命令）。</summary>
/// <param name="Id">CLI 给的任务 id。</param>
/// <param name="Description">任务说明。</param>
/// <param name="Kind">任务种类（claude：<c>local_bash</c>、<c>local_agent</c> 之类），原样转交。</param>
public sealed record CliBackgroundTask(string Id, string? Description, string? Kind);

/// <summary>一次上下文压缩。</summary>
/// <param name="Trigger"><c>manual</c>（用户发了 <c>/compact</c>）或 <c>auto</c>（上下文将满）；CLI 没说时为 null。</param>
/// <param name="PreTokens">压缩前的上下文 token 数。</param>
/// <param name="PostTokens">压缩后的上下文 token 数。</param>
public sealed record CliCompaction(string? Trigger, long? PreTokens, long? PostTokens);

/// <summary>
/// 一个等使用方回答的请求：CLI 要做一件档位不自动放行的事（<see cref="CliInteractionKinds.Permission"/>），
/// agent 问了用户一组问题（<see cref="CliInteractionKinds.Question"/>），或 agent 交出计划、问能不能开始（<see cref="CliInteractionKinds.Plan"/>）。
/// </summary>
/// <remarks>
/// 请求事件带 <see cref="RequestId"/>、<see cref="Kind"/>、<see cref="Options"/> 与 <see cref="ExpiresAt"/>，问题带 <see cref="Questions"/>、
/// 计划带 <see cref="Plan"/>；结局事件带同一个 <see cref="RequestId"/>、<see cref="Status"/> 与选中的 <see cref="OptionId"/>，问题的结局还带
/// <see cref="Answers"/>。回答经 <c>POST .../cli-runs/{runId}/permissions/{requestId}</c> 或聊天的
/// <c>POST chat/runs/{runId}/permissions/{requestId}</c>，从 <see cref="Options"/> 里挑一个（问题要同时给出 <c>answers</c>）。
/// 三种请求共用这一个形状、同一个回答接口、同一套超时 / 取消 / 重新附着的规则。
/// </remarks>
public sealed record CliAgentPermission
{
    /// <summary>框架给这个请求的 id：回答接口用它。</summary>
    public Guid? RequestId { get; init; }

    /// <summary>请求的种类：<see cref="CliInteractionKinds"/> 之一。缺省（此前落库的请求）为 <see cref="CliInteractionKinds.Permission"/>。</summary>
    public string Kind { get; init; } = CliInteractionKinds.Permission;

    /// <summary><see cref="CliInteractionKinds.Question"/>：agent 问的问题，按它给出的顺序。其余种类为 null。</summary>
    public IReadOnlyList<CliQuestion>? Questions { get; init; }

    /// <summary>
    /// <see cref="CliInteractionKinds.Plan"/>：agent 交出的计划（markdown）。其余种类为 null。
    /// </summary>
    /// <remarks>
    /// CLI 的入参里没有计划时（实测 claude 2.1.317：这一轮没有 Write 工具，计划文件写不出来，<c>ExitPlanMode</c> 的入参是 <c>{}</c>），
    /// 框架用 agent 进入计划档以来在主对话里写下的文字补上，<see cref="PlanSource"/> 说是哪一种。两种都没有时为 null。
    /// </remarks>
    public string? Plan { get; init; }

    /// <summary><see cref="Plan"/> 从哪里来：<see cref="CliPlanSources"/> 之一；没有计划时为 null。</summary>
    public string? PlanSource { get; init; }

    /// <summary>
    /// 问题的结局事件：交给 agent 的回答，问题原文 → 选中的选项标签（多选时多个；用户自己写的回答原样放在里面）。
    /// 拒绝回答、超时、随运行结束时为 null。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Answers { get; init; }

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
/// 等使用方回答的请求有哪几种（<see cref="CliAgentPermission.Kind"/>）。
/// </summary>
public static class CliInteractionKinds
{
    /// <summary>CLI 要做一件档位不自动放行的事（「先问我」档、计划获批之后的「改动前先问」）。</summary>
    public const string Permission = "permission";

    /// <summary>agent 问用户一组带选项的问题（claude 的 <c>AskUserQuestion</c>），回答交回给 agent 接着做。</summary>
    public const string Question = "question";

    /// <summary>agent 交出计划、问能不能开始（claude 的 <c>ExitPlanMode</c>）：批准就开始做，拒绝（可附意见）就接着改计划。</summary>
    public const string Plan = "plan";
}

/// <summary>
/// 计划请求上 <see cref="CliAgentPermission.Plan"/> 的来源（<see cref="CliAgentPermission.PlanSource"/>）。
/// </summary>
public static class CliPlanSources
{
    /// <summary>
    /// agent 交出的计划本身：claude <c>ExitPlanMode</c> 的 <c>plan</c>。先看 CLI 的批准请求，请求里没有时看 agent 那次工具调用的参数
    /// （实测 claude 2.1.317 会把 agent 传的 <c>plan</c> 从批准请求的入参里去掉，只剩 <c>{}</c>；工具调用帧里还在）。
    /// </summary>
    public const string Input = "input";

    /// <summary>
    /// CLI 没交出计划：框架取 agent 进入计划档以来在主对话里写下的全部文字（子 agent 的不算，思考不算），按出现顺序以空行相连。
    /// 被打回、改了再交的计划里包括改之前的那一版：agent 改计划时未必再把全文写一遍（实测有只在思考里改、直接再交的）。
    /// </summary>
    public const string AssistantText = "assistant_text";

    /// <summary>
    /// CLI 没交出计划、agent 进入计划档以来也没写回复文字：框架取这段时间里 CLI 发出的思考内容（同样只看主对话）。
    /// 实测 claude 2.1.317 被要求「把计划写出来」之后有时把计划写进了思考而不是回复；思考是 agent 的草稿，界面应当说明这一点。
    /// CLI 不发思考内容时（只发签名）这一来源为空。
    /// </summary>
    public const string AssistantThinking = "assistant_thinking";
}

/// <summary>
/// 问题与计划请求上框架给出的选项 id（<see cref="CliPermissionOption.Id"/>）。权限请求的选项 id 与种类同名，见 <see cref="CliPermissionOptionKinds"/>。
/// </summary>
public static class CliInteractionOptionIds
{
    /// <summary>问题：交出回答（回答体里的 <c>answers</c> 必填）。</summary>
    public const string Submit = "submit";

    /// <summary>问题：不回答。agent 收到「用户没有回答」，不会拿到空回答去猜。</summary>
    public const string Decline = "decline";

    /// <summary>计划：批准，接着用这次运行原来的档位（以「计划」档启动的运行：改动前逐个问）。</summary>
    public const string Approve = "approve";

    /// <summary>计划：批准，并且这个会话里的文件改动不再逐个问（claude 的 <c>acceptEdits</c>）。只在以「计划」档启动的运行上提供。</summary>
    public const string ApproveAcceptEdits = "approve_accept_edits";

    /// <summary>计划：不批准，agent 接着改计划；回答体里的 <c>message</c> 作为意见交给它。</summary>
    public const string Reject = "reject";
}

/// <summary>
/// agent 问的一个问题（claude <c>AskUserQuestion</c> 的一项）。
/// </summary>
/// <param name="Question">问题原文。回答按它对应（<c>answers</c> 的键）。</param>
/// <param name="Header">很短的标签（如 "Auth method"），界面拿来做标题；没给时为 null。</param>
/// <param name="Options">可选的回答。用户也可以不选、自己写一句（与 claude 本机界面的 "Other" 相同）。</param>
/// <param name="MultiSelect">true = 可以选多个。</param>
public sealed record CliQuestion(string Question, string? Header, IReadOnlyList<CliQuestionOption> Options, bool MultiSelect);

/// <summary>
/// 一个问题的一个可选回答。
/// </summary>
/// <param name="Label">选项文字，回答时原样带回。</param>
/// <param name="Description">这个选项意味着什么；没给时为 null。</param>
public sealed record CliQuestionOption(string Label, string? Description);

/// <summary>
/// 一次运行结束时上下文窗口的占用（claude 本机 <c>/context</c> 报的那个数）。
/// </summary>
/// <param name="Tokens">主对话最后一次模型调用的输入与输出 token 之和：下一轮开始时上下文里已经有这么多。</param>
/// <param name="Window">那个模型的上下文窗口大小；CLI 没报时为 null。</param>
/// <param name="Model">那次调用的模型。</param>
public sealed record CliContextUsage(long Tokens, long? Window, string? Model);

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
/// CLI 在握手时自己报的能力（ACP：<c>initialize</c> 的 <c>agentCapabilities</c> 与 <c>session/new</c> 的会话选项）。
/// </summary>
/// <remarks>
/// 报的是这个版本的 CLI 声称支持什么，不是实测：图片、推理强度这些按它来决定发不发，
/// 而「接了某个模型端点之后实际能不能用」要使用方自己实测、自己记。
/// </remarks>
public sealed record CliAgentCapabilities
{
    /// <summary>CLI 报的名字（ACP <c>agentInfo.name</c>）。</summary>
    public string? AgentName { get; init; }

    /// <summary>CLI 报的版本（ACP <c>agentInfo.version</c>）。</summary>
    public string? AgentVersion { get; init; }

    /// <summary>提示里收不收图片块。</summary>
    public bool Images { get; init; }

    /// <summary>提示里收不收音频块。</summary>
    public bool Audio { get; init; }

    /// <summary>提示里收不收内嵌资源（文件内容随提示带过去）。</summary>
    public bool EmbeddedContext { get; init; }

    /// <summary>能不能续接一个已有的会话（<c>session/resume</c> 或 <c>loadSession</c>）。</summary>
    public bool ResumeSession { get; init; }

    /// <summary>MCP server 收不收 HTTP 传输（stdio 是基础能力，不报）。</summary>
    public bool McpHttp { get; init; }

    /// <summary>MCP server 收不收 SSE 传输。</summary>
    public bool McpSse { get; init; }

    /// <summary>推理强度的可选值（会话选项里 category 为 <c>thought_level</c> 的那一项）；空 = 不能调。</summary>
    public IReadOnlyList<string> ReasoningEfforts { get; init; } = [];

    /// <summary>可选的模型（会话选项里 category 为 <c>model</c> 的那一项）；空 = 没报。</summary>
    public IReadOnlyList<string> Models { get; init; } = [];
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
    /// 这一轮结束时上下文窗口的占用；协议不报（ACP）或这一轮没有一次完整的模型调用时为 null。
    /// </summary>
    public CliContextUsage? Context { get; init; }

    /// <summary>
    /// true = <see cref="Usage"/> 是 CLI 报的会话累计值，不是这一轮的。
    /// </summary>
    /// <remarks>
    /// claude 的 <c>result.modelUsage</c> 就是这样：续接（含截断续接分叉出的新会话）的进程从会话自己保存的总量接着算，
    /// 第一帧 result 就已经带着之前每一轮。没有续接时它等于这一轮。
    /// </remarks>
    public bool UsageIsSessionTotal { get; init; }

    /// <summary>
    /// CLI 在这次握手里报的能力；协议不报（stream-json）或握手没走到那一步时为 null。执行器把它记在运行时上（<c>CliRuntimeDto.Capabilities</c>）。
    /// </summary>
    public CliAgentCapabilities? Capabilities { get; init; }

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

    /// <summary>这次运行里最后一次上下文压缩；没有压缩时为 null。</summary>
    /// <remarks>
    /// 一轮只做了压缩（<c>/compact</c>）时没有回答文本：<see cref="Output"/> 为 null，写进线程的是一条状态为
    /// <c>compacted</c> 的空回复，界面据此显示「对话已压缩」。
    /// </remarks>
    public CliCompaction? Compaction { get; init; }

    /// <summary>
    /// 最后一轮是 CLI 自己处理的本地命令（claude 的 <c>result.local_command</c>，如 <c>compact</c>），不是一次模型回答；否则为 null。
    /// </summary>
    public string? LocalCommand { get; init; }
}
