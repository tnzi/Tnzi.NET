namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 定时在执行宿主的宿主级目标上探测外部 agent CLI（本机宿主 = 这台机器的 PATH），注册/更新运行时记录。
/// </summary>
/// <remarks>
/// 定时而不是只在启动时探测：CLI 会被安装、卸载、升级换路径，而管理员不会因为
/// 装了个新 CLI 就去重启 API 进程。心跳（<c>LastSeenAt</c>）同时让「这台宿主还在不在」
/// 这个问题在远程 daemon 场景下有统一答案。
/// </remarks>
public class CliRuntimeProbeService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<CliAgentOptions> _options;
    private readonly ILogger<CliRuntimeProbeService> _logger;

    /// <summary>初始化探测服务。</summary>
    public CliRuntimeProbeService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<CliAgentOptions> options,
        ILogger<CliRuntimeProbeService> logger)
    {
        _scopeFactory = Check.NotNull(scopeFactory);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>AI:Cli:Enabled</c> 每轮重读：关着时按 <c>PollInterval</c> 空转等它打开，而不是在启动时读一次就退出，
    /// 那样运行期打开开关后，这台宿主上的 CLI 永远不会被登记成运行时。
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.Zero;
            try
            {
                var options = _options.CurrentValue;
                if (!options.Enabled)
                {
                    interval = options.PollInterval;
                }
                else
                {
                    interval = options.ProbeInterval;
                    await ProbeOnceAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 探测失败（含运行期改出一份通不过校验的配置）只影响可用性展示，不该让后台服务停掉。
                _logger.LogWarning(ex, "CLI runtime probe failed; will retry on the next interval");
            }

            try
            {
                await Task.Delay(interval > TimeSpan.Zero ? interval : new CliAgentOptions().ProbeInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProbeOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var probe = scope.ServiceProvider.GetRequiredService<ICliRuntimeService>();
        var result = await probe.ProbeAsync(stoppingToken);

        if (result.Succeeded && result.Data is not null)
        {
            _logger.LogDebug(
                "CLI runtime probe registered {Found} runtime(s); {Missing} provider(s) not on PATH",
                result.Data.Runtimes.Count, result.Data.NotFound.Count);
        }
    }
}
