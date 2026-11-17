namespace Tnzi.AI.Services;

/// <summary>
/// 应用决定一条排队消息能不能交给正在跑的那一轮（写进它的 CLI 进程、agent 在下一步之间收下），还是要等那一轮结束、作为新一轮发出。
/// </summary>
/// <remarks>
/// <para>
/// 交给正在跑的一轮的消息只是一句话：这一轮的模型、权限档、工具与每轮元数据在进程起来时就定了，消息自己选的模型与带的每轮元数据
/// 在这一轮里都不起作用，消费方的 <c>ICliAgentTurnHandler</c> 也不会为它再跑一次。所以框架的默认判断是：带着
/// <c>AI:Chat:Queue:StrictestValues</c> 里任何一个键的消息（那些键是「这一轮按什么规矩跑」）不交，选了与这一轮不同的推理强度的消息
/// （<see cref="ChatQueueHandoverRequest.Effort"/> 与 <see cref="ChatQueueHandoverRequest.RunEffort"/>）也不交，等新的一轮；其余的交。
/// 消费方的每轮元数据还有别的只有新一轮才用得上的值（例如这条消息点名的几台设备、要随它带上的上下文）时，注册本接口自己判断。
/// </para>
/// <para>
/// 带着文件（不是图片或 PDF、agent 用工具打开的附件）的消息一律等新的一轮，不问本接口：文件要在起进程之前放到执行目标上
/// （<c>CliLaunchProfile.AttachmentPaths</c>），运行中途没有放的地方。
/// </para>
/// <para>
/// 在执行这一轮的服务端副本上、交出之前调用，也在列出队列时调用（界面据此说出「立即发送」会不会停下这一轮）。它跑在后台作用域里，
/// 没有当前用户与 HTTP 请求：身份在 <see cref="ChatQueueHandoverRequest.UserId"/> 里。可以注册多个，全部同意才交。
/// </para>
/// </remarks>
public interface IChatQueueHandoverPolicy
{
    /// <summary>这条消息能不能交给正在跑的这一轮。</summary>
    /// <param name="request">消息、它要交给的运行，以及框架的默认判断。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<bool> CanHandOverAsync(ChatQueueHandoverRequest request, CancellationToken cancellationToken = default);
}

/// <summary>交给 <see cref="IChatQueueHandoverPolicy"/> 判断的一条排队消息。</summary>
/// <param name="ThreadId">线程。</param>
/// <param name="UserId">线程主人（消息以这个身份写下）。</param>
/// <param name="AgentId">线程的 Agent；不绑 Agent 时为 null。</param>
/// <param name="RunId">要交给的那一轮（外部运行）。</param>
/// <param name="MessageId">排队消息。</param>
/// <param name="Text">正文。</param>
/// <param name="Metadata">消息带的每轮元数据；没有时为 null。</param>
/// <param name="Model">消息选的模型；null = 线程默认。</param>
/// <param name="DefaultDecision">框架的默认判断（见 <see cref="IChatQueueHandoverPolicy"/>）。</param>
public sealed record ChatQueueHandoverRequest(
    Guid ThreadId,
    Guid UserId,
    Guid? AgentId,
    Guid RunId,
    Guid MessageId,
    string Text,
    IReadOnlyDictionary<string, JsonElement>? Metadata,
    string? Model,
    bool DefaultDecision)
{
    /// <summary>消息选的推理强度；null = 不带（随这一轮的）。</summary>
    public string? Effort { get; init; }

    /// <summary>正在跑的这一轮用的推理强度（这一轮选的，没选时是绑定上的）；null = 没给。</summary>
    /// <remarks>
    /// 强度钉在这一轮的进程上（stream-json 的 <c>--effort</c>），一轮进行中交进去的消息按这一轮的强度回答。
    /// 默认判断对 <see cref="Effort"/> 不为 null 且与它不同的消息为不交：它等这一轮结束、作为新的一轮以自己的强度发出。
    /// </remarks>
    public string? RunEffort { get; init; }
}
