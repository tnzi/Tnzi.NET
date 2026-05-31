namespace Tnzi.AspNetCore;

/// <summary>
/// 在主机优雅关闭阶段确定性地执行 Tnzi 各模块的 <c>OnApplicationShutdownAsync</c>，
/// 而非仅依赖 DI 容器释放时的 (同步) Dispose 路径。
/// <para>
/// <see cref="ITnziApplication.ShutdownAsync"/> 是幂等的：即使随后 DI 释放再次触发关闭，也是安全的 no-op。
/// 这样无论宿主以何种方式停止（Ctrl+C、SIGTERM、IIS 回收），模块关闭逻辑都会以异步方式可靠运行，
/// 不再退化到 <c>Task.Run(ShutdownAsync).GetAwaiter().GetResult()</c> 的 sync-over-async 兜底路径。
/// </para>
/// </summary>
internal sealed class TnziShutdownHostedService : IHostedService
{
    private readonly ITnziApplication _application;
    private readonly ILogger<TnziShutdownHostedService> _logger;

    public TnziShutdownHostedService(ITnziApplication application, ILogger<TnziShutdownHostedService> logger)
    {
        _application = Check.NotNull(application);
        _logger = Check.NotNull(logger);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _application.ShutdownAsync();
        }
        catch (Exception ex)
        {
            // 关闭失败不应抛出以免中断宿主停止流程，但必须记录（不再静默吞掉）
            _logger.LogError(ex, "Error while shutting down Tnzi application modules during host stop");
        }
    }
}
