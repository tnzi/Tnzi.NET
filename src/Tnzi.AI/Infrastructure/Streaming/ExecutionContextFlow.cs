namespace Tnzi.AI.Infrastructure.Streaming;

/// <summary>
/// 让异步迭代器的每一步都在「开始枚举那一刻」的执行上下文里恢复执行。
/// </summary>
/// <remarks>
/// <para>
/// async iterator 的每次 <c>MoveNextAsync</c> 都在<b>调用方当时</b>的 <see cref="ExecutionContext"/> 上恢复：
/// 迭代器体内设置的 AsyncLocal（<see cref="IAgentExecutionContextAccessor"/> 的当前请求与属性包、
/// <c>ThinkingRequestPolicy</c>、<c>ToolContextAccessor</c>）只活到第一个 <c>yield</c>。
/// 流式执行器总是先 yield 一个 chunk（文本 / 工具调用提示）再执行工具，于是工具与工具循环里后续的
/// LLM 调用都跑在消费方（控制器、ChatService）的上下文里：取不到当前请求、属性包是空的。
/// 绑定到用户的权限规则因此取不到 UserId 而不匹配，同一个工具调用在非流式下被拒、在流式下放行。
/// </para>
/// <para>
/// 普通 async 方法没有这个问题：它的续体始终带着自己的上下文。这里把同样的语义补给迭代器：
/// 在 <c>GetAsyncEnumerator</c> 时捕获上下文，此后每次 <c>MoveNextAsync</c> / <c>DisposeAsync</c>
/// 都先还原它再推进被包装的迭代器。还原发生在一个 async 方法内部，方法同步返回时运行时会把调用方的
/// 上下文还原回去，所以不会泄漏给消费方。
/// </para>
/// <para>
/// 管线的每一层都要包：外层中间件在 before 段设置的 AsyncLocal 只出现在它调用 <c>next</c> 那一刻的上下文里，
/// 内层（直到执行器与工具）要在每一步都看到它，捕获点就必须是那一刻。
/// </para>
/// </remarks>
internal static class ExecutionContextFlow
{
    /// <summary>
    /// 包装一个异步序列：枚举开始时捕获当前执行上下文，之后每一步都在该上下文里推进源序列。
    /// 上下文流动被抑制（<see cref="ExecutionContext.SuppressFlow"/>）时原样返回源序列的枚举器。
    /// </summary>
    public static IAsyncEnumerable<T> Bind<T>(IAsyncEnumerable<T> source)
    {
        Check.NotNull(source);
        return new BoundEnumerable<T>(source);
    }

    private sealed class BoundEnumerable<T>(IAsyncEnumerable<T> source) : IAsyncEnumerable<T>
    {
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            var context = ExecutionContext.Capture();
            var inner = source.GetAsyncEnumerator(cancellationToken);
            return context == null ? inner : new BoundEnumerator<T>(inner, context);
        }
    }

    private sealed class BoundEnumerator<T>(IAsyncEnumerator<T> inner, ExecutionContext context) : IAsyncEnumerator<T>
    {
        public T Current => inner.Current;

        public async ValueTask<bool> MoveNextAsync()
        {
            ExecutionContext.Restore(context);
            return await inner.MoveNextAsync();
        }

        public async ValueTask DisposeAsync()
        {
            // 提前结束枚举时，源迭代器的 finally 段（结算、持久化、还原）也应在运行自己的上下文里执行
            ExecutionContext.Restore(context);
            await inner.DisposeAsync();
        }
    }
}
