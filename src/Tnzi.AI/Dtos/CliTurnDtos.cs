namespace Tnzi.AI.Dtos;

/// <summary>
/// 一轮即将交给外部 CLI 运行时的对话（<see cref="Services.ICliAgentTurnHandler.OnTurnAsync"/> 的输入）。
/// </summary>
public sealed record CliTurnContext
{
    /// <summary>执行这一轮的 Agent。</summary>
    public required Guid AgentId { get; init; }

    /// <summary>
    /// 这一轮所在的线程（已核过归属）。<c>null</c> = 这一轮会开一个新线程（新线程的 id 在
    /// <see cref="CliTurnQueued.ThreadId"/> 里给出），或是不建线程的一次性运行（<see cref="IsEphemeral"/>）。
    /// </summary>
    public Guid? ThreadId { get; init; }

    /// <summary>发起这一轮的用户；后台调用为 null。</summary>
    public Guid? UserId { get; init; }

    /// <summary>用户这一轮的文本。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>这一轮带的附件（已校验）。</summary>
    public IReadOnlyList<CliTurnAttachment> Attachments { get; init; } = [];

    /// <summary>
    /// 线程刚为这一轮回退过（编辑或重新生成）：被替换的那条用户消息及其后的消息已从线程删除，
    /// CLI 会话也会从它之前的那一轮接着走。按线程维护「已经告诉过 agent 什么」的消费方据此把那些内容重新带上。
    /// </summary>
    public bool RewoundTurns { get; init; }

    /// <summary>不建线程、不留消息的一次性运行（评估）。</summary>
    public bool IsEphemeral { get; init; }

    /// <summary>
    /// 客户端随这一轮带来的每轮元数据（<c>ChatRequestDto.Metadata</c>，已校验）；没带时为空。
    /// 键与值都由消费方定义，例如这条消息点名的几台设备的 id。
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Metadata { get; init; } = new Dictionary<string, JsonElement>();
}

/// <summary>
/// 这一轮带的一个附件。
/// </summary>
/// <param name="MediaType">媒体类型。</param>
/// <param name="FileName">文件名（客户端给的话）。</param>
/// <param name="Size">字节数。</param>
public sealed record CliTurnAttachment(string MediaType, string? FileName, long Size);

/// <summary>
/// 处理器对一轮的决定。
/// </summary>
public sealed record CliTurnDecision
{
    /// <summary>是否拒绝这一轮。</summary>
    public bool Refused { get; private init; }

    /// <summary>要追加到这一轮提示尾部的上下文（放行时）。</summary>
    public string? Context { get; private init; }

    /// <summary>拒绝的原因（拒绝时），原样交给客户端：写给用户看的英文。</summary>
    public string? Message { get; private init; }

    /// <summary>拒绝的错误码（拒绝时）；没给时为 <c>AI_CLI_TURN_REFUSED</c>。</summary>
    public string? ErrorCode { get; private init; }

    /// <summary>拒绝的 HTTP 状态码（拒绝时）。</summary>
    public int StatusCode { get; private init; } = 200;

    /// <summary>放行，可带一段这一轮的上下文。</summary>
    public static CliTurnDecision Proceed(string? context = null) => new() { Context = context };

    /// <summary>拒绝这一轮。</summary>
    /// <param name="message">原因，写给用户看。</param>
    /// <param name="errorCode">错误码；null = <c>AI_CLI_TURN_REFUSED</c>。</param>
    /// <param name="statusCode">HTTP 状态码，默认 403。</param>
    public static CliTurnDecision Refuse(string message, string? errorCode = null, int statusCode = 403)
    {
        Check.NotNullOrWhiteSpace(message);
        return new CliTurnDecision
        {
            Refused = true,
            Message = message,
            ErrorCode = errorCode,
            StatusCode = statusCode is >= 400 and < 600 ? statusCode : 403
        };
    }
}

/// <summary>
/// 一轮已经交给外部 CLI 运行时（<see cref="Services.ICliAgentTurnHandler.OnTurnQueuedAsync"/> 的输入）。
/// </summary>
public sealed record CliTurnQueued
{
    /// <summary>外部运行 id。</summary>
    public required Guid RunId { get; init; }

    /// <summary>执行这一轮的 Agent。</summary>
    public required Guid AgentId { get; init; }

    /// <summary>这一轮所在的线程（新线程也已经有了）；一次性运行为 null。</summary>
    public Guid? ThreadId { get; init; }

    /// <summary>线程是不是为这一轮新建的。</summary>
    public bool IsNewThread { get; init; }

    /// <summary>存下的用户消息。</summary>
    public Guid? UserMessageId { get; init; }

    /// <summary>发起这一轮的用户。</summary>
    public Guid? UserId { get; init; }

    /// <summary>处理器们给这一轮补的上下文（拼接后），没有为 null。</summary>
    public string? Context { get; init; }

    /// <summary>线程刚为这一轮回退过（编辑或重新生成）。</summary>
    public bool RewoundTurns { get; init; }

    /// <summary>客户端随这一轮带来的每轮元数据（已随用户消息存下）；没带时为空。</summary>
    public IReadOnlyDictionary<string, JsonElement> Metadata { get; init; } = new Dictionary<string, JsonElement>();
}
