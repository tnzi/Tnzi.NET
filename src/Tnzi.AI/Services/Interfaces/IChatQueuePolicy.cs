namespace Tnzi.AI.Services;

/// <summary>
/// 应用决定这一次请求能不能往线程的消息队列里放东西：排一条新消息、改一条的正文、立即发送一条。
/// </summary>
/// <remarks>
/// <para>
/// 框架自己只判定线程归属（只有线程主人能碰它的队列）。有些会话即便是线程主人也不该排队：比如从一台临时设备、
/// 用一个临时口令登进来的会话，它可以看、可以当场发，但不该往队列里放一条在它离开之后、以主人的完整身份在后台发出的消息。
/// 「这是哪种会话」只有应用知道（它在登录凭据里），所以由应用判断。
/// </para>
/// <para>
/// 在 <see cref="IChatQueueService"/> 的新增、改正文、立即发送里、归属判定之后调用（列出与删除不调用：它们不会让任何东西发出去）。
/// 它是 Scoped 服务，跑在发起请求的那个作用域里，可以注入 <see cref="ICurrentUser"/>、<c>IHttpContextAccessor</c> 读当前会话的声明。
/// 拒绝时返回失败的 <see cref="Result"/>：没给状态码答 403，没给错误码答 <c>AI_QUEUE_NOT_ALLOWED</c>。
/// 没有注册实现时一律放行。
/// </para>
/// </remarks>
public interface IChatQueuePolicy
{
    /// <summary>放行返回成功；拒绝返回带说明（以及可选的状态码、错误码）的失败。</summary>
    /// <param name="request">这一次的操作、线程、用户、Agent。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result> CanQueueAsync(ChatQueueRequest request, CancellationToken cancellationToken = default);
}

/// <summary>交给 <see cref="IChatQueuePolicy"/> 的一次队列操作。</summary>
/// <param name="Operation">要做什么。</param>
/// <param name="ThreadId">线程（已核过归属）。</param>
/// <param name="UserId">线程主人，也就是发起请求的用户。</param>
/// <param name="AgentId">线程的 Agent；不绑 Agent 时为 null。</param>
/// <param name="MessageId">改正文、立即发送时是那条排队消息；新增时为 null。</param>
public sealed record ChatQueueRequest(ChatQueueOperation Operation, Guid ThreadId, Guid UserId, Guid? AgentId, Guid? MessageId);

/// <summary>交给 <see cref="IChatQueuePolicy"/> 判断的队列操作。</summary>
public enum ChatQueueOperation
{
    /// <summary>排一条新消息。</summary>
    Queue = 0,

    /// <summary>改一条排队消息的正文。</summary>
    Edit = 1,

    /// <summary>立即发送一条排队消息（交给正在跑的一轮；那一轮收不下时停掉它）。</summary>
    SendNow = 2
}
