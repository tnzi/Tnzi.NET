namespace Tnzi.Identity.Services;

/// <summary>
/// 会话维护后台服务：周期性清理过期认证令牌、撤销长期失活会话，
/// 避免幽灵会话/令牌累积（既节省存储，也让并发计数只统计真正活跃的会话）。
/// 与 OnTokenValidated 的实时强制校验互补：强制校验保证"被撤销即刻失效"，
/// 本服务负责"过期物件的最终清扫"。
/// </summary>
public class SessionMaintenanceBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<SessionOptions> _sessionOptions;
    private readonly IOptionsMonitor<IdentityOptions> _identityOptions;
    private readonly ILogger<SessionMaintenanceBackgroundService> _logger;

    public SessionMaintenanceBackgroundService(
        IServiceProvider serviceProvider,
        IOptionsMonitor<SessionOptions> sessionOptions,
        IOptionsMonitor<IdentityOptions> identityOptions,
        ILogger<SessionMaintenanceBackgroundService> logger)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _sessionOptions = Check.NotNull(sessionOptions);
        _identityOptions = Check.NotNull(identityOptions);
        _logger = Check.NotNull(logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = _sessionOptions.CurrentValue.MaintenanceIntervalMinutes;
        if (intervalMinutes <= 0)
        {
            _logger.LogInformation("Session maintenance is disabled (MaintenanceIntervalMinutes <= 0).");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(5, intervalMinutes));
        using var timer = new PeriodicTimer(interval);

        // 首个 tick 后再执行，避免与启动流程争抢连接。
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunMaintenanceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 维护失败不应终止后台循环 —— 记录并等待下个周期重试。
                _logger.LogError(ex, "Session maintenance pass failed; will retry next interval.");
            }
        }
    }

    private async Task RunMaintenanceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var services = scope.ServiceProvider;

        // 1) 清理过期认证令牌（含各会话已过期的刷新令牌）。
        var authTokenService = services.GetService<IAuthTokenService>();
        if (authTokenService != null)
        {
            var removed = await authTokenService.CleanExpiredTokensAsync();
            if (removed > 0)
            {
                _logger.LogInformation("Session maintenance: removed {Count} expired auth tokens.", removed);
            }
        }

        // 2) 撤销长期失活会话（阈值取刷新令牌生命周期：活跃会话每次刷新都会续期，
        //    超过该窗口未刷新的会话视为失活）。已过硬过期的会话本就被判定失效，此处一并收敛。
        var sessionService = services.GetService<ISessionService>();
        if (sessionService != null)
        {
            var refreshDays = Math.Max(1, _identityOptions.CurrentValue.Jwt.RefreshTokenExpirationDays);
            var result = await sessionService.CleanExpiredSessionsAsync(TimeSpan.FromDays(refreshDays));
            if (result.Succeeded && result.Data > 0)
            {
                _logger.LogInformation("Session maintenance: revoked {Count} inactive sessions.", result.Data);
            }
        }
    }
}
