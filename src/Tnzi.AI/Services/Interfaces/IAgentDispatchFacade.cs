namespace Tnzi.AI.Services;

/// <summary>
/// 执行路由门面。<b>整个框架里唯一允许判断「这个 Agent 走内建还是外部」的地方。</b>
/// </summary>
/// <remarks>
/// <para>
/// 签名刻意与 <see cref="IAgentRuntime"/> <b>逐字相同</b>，因此它是调用方的 drop-in 替换：
/// <c>ChatService</c> / <c>AgentService</c> 只把注入的类型换掉，一行执行逻辑都不用改。
/// 这不是巧合而是设计目的 —— 只要调用方能感知到「底下走了哪条路」，分支就会开始向外扩散，
/// 而上一版 <c>Tnzi.AI.Cli</c> 正是这样长出 28 处 <c>ShouldSkipMiddleware</c> 的。
/// </para>
/// <para>
/// 外部路径的返回值由 <c>Tnzi.AI.Cli</c> 把归一化的 <see cref="CliAgentEvent"/> 流翻译成
/// <see cref="AgentStreamChunk"/> / <see cref="AgentRunResult"/>；未加载子模块时
/// <c>ICliAgentBindingService</c> 的 NoOp 恒返回 null，于是这里恒走内建路径。
/// </para>
/// <para>
/// 约定测试 <c>CliAgentRedLineTests</c> 守住这条红线：全仓只有本接口的实现里
/// 可以同时引用 <see cref="IAgentRuntime"/> 与 <see cref="ICliAgentDispatcher"/>。
/// </para>
/// </remarks>
public interface IAgentDispatchFacade
{
    /// <summary>执行一次 AI 运行（非流式）。按绑定路由到内建管线或外部 agent。</summary>
    Task<AgentRunResult> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>执行一次 AI 运行（流式）。按绑定路由到内建管线或外部 agent。</summary>
    IAsyncEnumerable<AgentStreamChunk> RunStreamingAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default);
}
