namespace Tnzi.AI.Tools;

/// <summary>
/// 工具执行中间件 - 拦截工具调用的前后处理管道
/// </summary>
/// <remarks>
/// <para>
/// 中间件按注册顺序执行（洋葱模型），每个中间件可以在调用 <paramref name="next"/> 前后执行逻辑。
/// 典型用途：日志记录、耗时统计、速率限制、审计、权限检查、结果缓存等。
/// </para>
/// <para>
/// 示例：
/// <code>
/// public class LoggingToolMiddleware : IToolExecutionMiddleware
/// {
///     public async Task&lt;object?&gt; InvokeAsync(ToolExecutionContext context, Func&lt;Task&lt;object?&gt;&gt; next)
///     {
///         _logger.LogInformation("Tool {Name} started", context.ToolName);
///         var result = await next();
///         _logger.LogInformation("Tool {Name} completed in {Duration}ms", context.ToolName, context.Stopwatch.ElapsedMilliseconds);
///         return result;
///     }
/// }
/// </code>
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Tool middleware pipeline may evolve based on usage patterns")]
public interface IToolExecutionMiddleware
{
    /// <summary>
    /// 执行中间件逻辑
    /// </summary>
    /// <param name="context">工具执行上下文（包含工具名称、参数等信息）</param>
    /// <param name="next">调用下一个中间件或最终执行工具</param>
    /// <returns>工具执行结果</returns>
    Task<object?> InvokeAsync(ToolExecutionContext context, Func<Task<object?>> next);
}

/// <summary>
/// 工具执行上下文 - 传递给中间件的工具调用信息
/// </summary>
[ExperimentalApi(Reason = "Tool middleware pipeline may evolve based on usage patterns")]
public class ToolExecutionContext
{
    /// <summary>工具名称</summary>
    public string ToolName { get; init; } = string.Empty;

    /// <summary>工具所属分组</summary>
    public string? ToolGroup { get; init; }

    /// <summary>工具调用参数</summary>
    public IDictionary<string, object?>? Arguments { get; init; }

    /// <summary>调用 ID（LLM 分配的唯一标识）</summary>
    public string CallId { get; init; } = string.Empty;

    /// <summary>计时器（中间件可用于记录耗时）</summary>
    public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();

    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>扩展属性（中间件间传递数据）</summary>
    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();
}
