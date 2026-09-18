
namespace Tnzi.AI.Sandbox.Middleware;

/// <summary>
/// 沙箱生命周期中间件 - 为每次 Agent 运行发布一个<b>按需创建</b>的沙箱环境，运行结束时释放。
/// </summary>
/// <remarks>
/// <para>
/// 沙箱通过 <see cref="IAgentExecutionContextAccessor"/>（AsyncLocal 通道）以
/// <see cref="SandboxToolEnvironment"/> 形式发布给 <c>SandboxTools</c>：
/// 工具的 JSON schema 不暴露 <c>ISandbox</c>/<c>threadId</c> 环境参数，
/// 工具在调用时从环境解析。AsyncLocal 沿 next() 调用树流动，
/// 因此主管线和 AgentAsTools 子代理（在父运行内执行）都能看到同一环境；
/// next() 结束后条目在 finally 中移除、环境释放，避免悬挂已释放的沙箱引用。
/// </para>
/// <para>
/// ★ 发布的是环境不是沙箱：目录布置（<see cref="IThreadDataProvisioner"/>）与
/// <see cref="ISandboxProvider.CreateAsync"/> 都推迟到第一个工具真的要用沙箱那一刻。
/// 没有沙箱工具的 Agent、这一轮没用到沙箱的 Agent，这里的开销是一个对象；
/// provider 的拒绝（Production 下的 Local 守卫、Docker 不可达）也只落在真的要用它的那次工具调用上，
/// 由执行器按「工具失败」记 Error 日志并回给模型，而不是让每一次运行在入口就崩掉。
/// </para>
/// </remarks>
public class SandboxMiddleware : IAiMiddleware
{
    private readonly ISandboxProvider _provider;
    private readonly IOptions<SandboxModuleOptions> _options;
    private readonly IAgentExecutionContextAccessor _executionContextAccessor;
    private readonly IThreadDataProvisioner _provisioner;
    private readonly ILogger<SandboxMiddleware> _logger;

    public int Order => AiMiddlewareOrders.Sandbox;

    public SandboxMiddleware(
        ISandboxProvider provider,
        IOptions<SandboxModuleOptions> options,
        IAgentExecutionContextAccessor executionContextAccessor,
        IThreadDataProvisioner provisioner,
        ILogger<SandboxMiddleware> logger)
    {
        _provider = Check.NotNull(provider);
        _options = Check.NotNull(options);
        _executionContextAccessor = Check.NotNull(executionContextAccessor);
        _provisioner = Check.NotNull(provisioner);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(
        AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        if (!_options.Value.Enabled) return await next(context, cancellationToken);

        var threadData = context.Properties.GetValueOrDefault(SandboxPropertyKeys.ThreadData) as ThreadDataState;
        if (threadData is null) return await next(context, cancellationToken);

        var threadId = context.Request.ThreadId ?? Guid.NewGuid();
        await using var environment = CreateEnvironment(threadId, threadData);
        PublishToolEnvironment(environment);

        try
        {
            return await next(context, cancellationToken);
        }
        finally
        {
            RemoveToolEnvironment();
            LogReleased(environment);
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
        await using var environment = CreateEnvironment(threadId, threadData);
        PublishToolEnvironment(environment);

        try
        {
            await foreach (var chunk in next(context, cancellationToken))
                yield return chunk;
        }
        finally
        {
            RemoveToolEnvironment();
            LogReleased(environment);
        }
    }

    /// <summary>
    /// 环境的工厂：先布置线程目录（建目录、复制技能资源），再向 provider 要沙箱。
    /// 只在第一个工具调用 <see cref="SandboxToolEnvironment.GetSandboxAsync"/> 时执行。
    /// </summary>
    private SandboxToolEnvironment CreateEnvironment(Guid threadId, ThreadDataState threadData)
    {
        return new SandboxToolEnvironment(threadId, async ct =>
        {
            await _provisioner.ProvisionAsync(threadId, threadData, ct);

            var sandbox = await _provider.CreateAsync(new SandboxCreateOptions
            {
                ThreadId = threadId,
                WorkspacePath = threadData.ThreadDirectory
            }, ct);

            _logger.LogDebug("Sandbox {SandboxId} created on first use for thread {ThreadId}", sandbox.Id, threadId);
            return sandbox;
        });
    }

    /// <summary>
    /// 把沙箱环境发布到执行上下文属性包（工具调用时读取）。
    /// </summary>
    private void PublishToolEnvironment(SandboxToolEnvironment environment)
    {
        _executionContextAccessor.Properties[SandboxPropertyKeys.ToolEnvironment] = environment;
    }

    /// <summary>
    /// next() 结束后移除环境条目 - 环境即将被释放，绝不允许悬挂引用存活。
    /// </summary>
    private void RemoveToolEnvironment()
    {
        _executionContextAccessor.Properties.Remove(SandboxPropertyKeys.ToolEnvironment);
    }

    private void LogReleased(SandboxToolEnvironment environment)
    {
        if (environment.IsSandboxCreated)
            _logger.LogDebug("Sandbox environment for thread {ThreadId} released", environment.ThreadId);
        else
            _logger.LogDebug("Sandbox environment for thread {ThreadId} released without ever creating a sandbox", environment.ThreadId);
    }
}
