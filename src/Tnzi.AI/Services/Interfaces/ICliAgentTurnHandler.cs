namespace Tnzi.AI.Services;

/// <summary>
/// 消费方在「一轮对话交给外部 CLI 运行时」前后插手的扩展点。
/// </summary>
/// <remarks>
/// <para>
/// 外部执行按设计不进中间件管线，内建路径上那些按轮次插手的位置（上下文注入、守卫、历史）在这里一个都没有。
/// 这个接口就是外部路径上对应的那个位置：路由门面在把一轮聊天交给 CLI 之前、之后各调一次。
/// 能做三件事：
/// </para>
/// <list type="number">
/// <item><b>给这一轮补上下文</b>（<see cref="CliTurnDecision.Proceed"/> 的 context）：每轮都变的信息（当前时间、
/// 这一轮能用哪些设备……）追加在提示尾部，<b>不进</b> Agent 的说明（那段在 prompt cache 的前缀里，一变就作废整段缓存）。
/// 多个处理器的上下文按注册顺序以空行拼接。</item>
/// <item><b>拒绝这一轮</b>（<see cref="CliTurnDecision.Refuse"/>）：例如受限会话只许用某些线程。拒绝时线程里什么都不写，
/// 客户端收到给出的状态码与错误码。第一个拒绝的处理器说了算，后面的不再调用。</item>
/// <item><b>得知这一轮已入队</b>（<see cref="OnTurnQueuedAsync"/>）：线程 id（新线程此时才有）、运行 id、用户消息 id，
/// 用来让消费方按线程维护的状态跟上轮次。它在运行入队之后调用，抛出的异常只记日志，不影响这一轮。</item>
/// </list>
/// <para>
/// 处理器在门面为这一轮开的那个 DI 作用域里解析（线程与运行也在那里写下、立即提交），不在 HTTP 请求的作用域里：
/// 它写下的东西跟运行一起落定，不随一个可能持续几十分钟的流式请求的事务悬着。注册多个即全部生效。
/// </para>
/// <para>只对绑定了外部 CLI 运行时的 Agent 调用；内建引擎用中间件与上下文提供者做同样的事。</para>
/// </remarks>
public interface ICliAgentTurnHandler
{
    /// <summary>这一轮入队之前：补上下文，或拒绝。</summary>
    Task<CliTurnDecision> OnTurnAsync(CliTurnContext turn, CancellationToken cancellationToken = default);

    /// <summary>这一轮入队之后。默认什么都不做。</summary>
    Task OnTurnQueuedAsync(CliTurnQueued queued, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
