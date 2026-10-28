namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 驱动 <see cref="AgentRunRecoveryService"/> 的后台循环：每隔 <see cref="ScanInterval"/> 扫描一次孤儿运行。
/// </summary>
/// <remarks>
/// 多实例部署下每个实例都会扫描，这是安全的：回收是幂等的条件更新。
/// 一次扫描失败只记日志、循环继续，因为循环退出不会有任何症状，只是孤儿运行再也没人回收。
/// </remarks>
public class AgentRunRecoveryHostedService : BackgroundService
{
    /// <summary>扫描间隔。</summary>
    public static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentRunRecoveryHostedService> _logger;

    public AgentRunRecoveryHostedService(IServiceScopeFactory scopeFactory, ILogger<AgentRunRecoveryHostedService> logger)
    {
        _scopeFactory = Check.NotNull(scopeFactory);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ScanOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 关停，正常路径
        }
    }

    private async Task ScanOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var recovery = scope.ServiceProvider.GetRequiredService<AgentRunRecoveryService>();
            await recovery.RecoverOrphanedRunsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent run recovery scan failed; the scheduler keeps running.");
        }
    }
}
