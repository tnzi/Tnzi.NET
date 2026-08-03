namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 定时探测本宿主 PATH 上的外部 agent CLI，注册/更新运行时记录。
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
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 探测失败只影响可用性展示，不该让后台服务停掉。
                _logger.LogWarning(ex, "CLI runtime probe failed; will retry on the next interval");
            }

            try
            {
                await Task.Delay(_options.CurrentValue.ProbeInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
