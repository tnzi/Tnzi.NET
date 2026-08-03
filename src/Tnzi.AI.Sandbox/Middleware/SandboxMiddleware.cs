
namespace Tnzi.AI.Sandbox.Middleware;

/// <summary>
/// 沙箱生命周期中间件 - 为每次 Agent 运行创建/释放沙箱实例。
/// </summary>
/// <remarks>
/// 沙箱通过 <see cref="IAgentExecutionContextAccessor"/>（AsyncLocal 通道）以
/// <see cref="SandboxToolEnvironment"/> 形式发布给 <c>SandboxTools</c>：
/// 工具的 JSON schema 不再暴露 <c>ISandbox</c>/<c>threadId</c> 环境参数，
/// 工具在调用时从环境解析。AsyncLocal 沿 next() 调用树流动，
/// 因此主管线和 AgentAsTools 子代理（在父运行内执行）都能看到同一沙箱；
/// next() 结束后条目在 finally 中移除，避免悬挂已释放的沙箱引用。
/// </remarks>
public class SandboxMiddleware : IAiMiddleware
{
    private readonly ISandboxProvider _provider;
    private readonly IOptions<SandboxModuleOptions> _options;
    private readonly IAgentExecutionContextAccessor _executionContextAccessor;
    private readonly ILogger<SandboxMiddleware> _logger;

    public int Order => AiMiddlewareOrders.Sandbox;

    public SandboxMiddleware(
        ISandboxProvider provider,
        IOptions<SandboxModuleOptions> options,
        IAgentExecutionContextAccessor executionContextAccessor,
        ILogger<SandboxMiddleware> logger)
    {
        _provider = Check.NotNull(provider);
        _options = Check.NotNull(options);
        _executionContextAccessor = Check.NotNull(executionContextAccessor);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(
        AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        if (!_options.Value.Enabled) return await next(context, cancellationToken);

        var threadData = context.Properties.GetValueOrDefault(SandboxPropertyKeys.ThreadData) as ThreadDataState;
        if (threadData is null) return await next(context, cancellationToken);

        var threadId = context.Request.ThreadId ?? Guid.NewGuid();
        await using var sandbox = await _provider.CreateAsync(new SandboxCreateOptions
        {
            ThreadId = threadId,
            WorkspacePath = threadData.ThreadDirectory
        }, cancellationToken);

        PublishToolEnvironment(sandbox, threadId);
        _logger.LogDebug("Sandbox {SandboxId} acquired for thread {ThreadId}", sandbox.Id, threadId);

        try
        {
            return await next(context, cancellationToken);
        }
        finally
        {
            RemoveToolEnvironment();
            _logger.LogDebug("Sandbox {SandboxId} released", sandbox.Id);
        }
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context, AiStreamingMiddlewareDelegate next,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_options.Value.Enabled)
        {
            await foreach (var chunk in next(context, cancellationToken))
                yield return chunk;
            yield break;
        }

        var threadData = context.Properties.GetValueOrDefault(SandboxPropertyKeys.ThreadData) as ThreadDataState;
        if (threadData is null)
        {
            await foreach (var chunk in next(context, cancellationToken))
                yield return chunk;
            yield break;
        }

        var threadId = context.Request.ThreadId ?? Guid.NewGuid();
        await using var sandbox = await _provider.CreateAsync(new SandboxCreateOptions
        {
            ThreadId = threadId,
            WorkspacePath = threadData.ThreadDirectory
        }, cancellationToken);

        PublishToolEnvironment(sandbox, threadId);
        _logger.LogDebug("Sandbox {SandboxId} acquired for thread {ThreadId}", sandbox.Id, threadId);

        try
        {
            await foreach (var chunk in next(context, cancellationToken))
                yield return chunk;
        }
        finally
        {
            RemoveToolEnvironment();
            _logger.LogDebug("Sandbox {SandboxId} released", sandbox.Id);
        }
    }

    /// <summary>
    /// 把活动沙箱环境发布到执行上下文属性包（工具调用时读取）。
    /// </summary>
    private void PublishToolEnvironment(ISandbox sandbox, Guid threadId)
    {
        _executionContextAccessor.Properties[SandboxPropertyKeys.ToolEnvironment] =
            new SandboxToolEnvironment(sandbox, threadId);
    }

    /// <summary>
    /// next() 结束后移除环境条目 - 沙箱即将被释放，绝不允许悬挂引用存活。
    /// </summary>
    private void RemoveToolEnvironment()
    {
        _executionContextAccessor.Properties.Remove(SandboxPropertyKeys.ToolEnvironment);
    }
}
