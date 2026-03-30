
namespace Tnzi.AI.Sandbox.Middleware;

public class SandboxMiddleware : IAiMiddleware
{
    private readonly ISandboxProvider _provider;
    private readonly IOptions<SandboxModuleOptions> _options;
    private readonly ILogger<SandboxMiddleware> _logger;

    public int Order => AiMiddlewareOrders.Sandbox;

    public SandboxMiddleware(
        ISandboxProvider provider,
        IOptions<SandboxModuleOptions> options,
        ILogger<SandboxMiddleware> logger)
    {
        _provider = Check.NotNull(provider);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(
        AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        if (context.ShouldSkipMiddleware || !_options.Value.Enabled) return await next(context, cancellationToken);

        var threadData = context.Properties.GetValueOrDefault(SandboxPropertyKeys.ThreadData) as ThreadDataState;
        if (threadData is null) return await next(context, cancellationToken);

        await using var sandbox = await _provider.CreateAsync(new SandboxCreateOptions
        {
            ThreadId = context.Request.ThreadId ?? Guid.NewGuid(),
            WorkspacePath = threadData.ThreadDirectory
        }, cancellationToken);

        context.Properties[SandboxPropertyKeys.Sandbox] = sandbox;
        context.Properties[SandboxPropertyKeys.SandboxId] = sandbox.Id;
        _logger.LogDebug("Sandbox {SandboxId} acquired for thread", sandbox.Id);

        try
        {
            return await next(context, cancellationToken);
        }
        finally
        {
            _logger.LogDebug("Sandbox {SandboxId} released", sandbox.Id);
        }
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context, AiStreamingMiddlewareDelegate next,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (context.ShouldSkipMiddleware || !_options.Value.Enabled)
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

        await using var sandbox = await _provider.CreateAsync(new SandboxCreateOptions
        {
            ThreadId = context.Request.ThreadId ?? Guid.NewGuid(),
            WorkspacePath = threadData.ThreadDirectory
        }, cancellationToken);

        context.Properties[SandboxPropertyKeys.Sandbox] = sandbox;
        context.Properties[SandboxPropertyKeys.SandboxId] = sandbox.Id;

        await foreach (var chunk in next(context, cancellationToken))
            yield return chunk;
    }
}
