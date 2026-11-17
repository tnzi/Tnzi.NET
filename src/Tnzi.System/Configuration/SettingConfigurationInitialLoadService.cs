namespace Tnzi.System.Configuration;

/// <summary>
/// 首次从数据库填充设置失败时，在后台退避重试直到读到为止。
/// </summary>
/// <remarks>
/// 启动那一刻数据库不可达（重启、切换站点后出口 IP 被白名单挡住）时，<see cref="SettingConfigurationSource.AttachAsync"/>
/// 只记一条 Warning、不打断启动，应用用 appsettings 的值先跑。没有这条重试，它就一直用这些默认值跑到下次重启，
/// 而看上去一切健康。读到之后 <see cref="SettingConfigurationProvider"/> 替换数据并触发 reload token，
/// <c>IOptionsMonitor</c> 的订阅者收到与一次设置变更相同的通知。
/// 间隔从 <see cref="SettingConfigurationOptions.InitialLoadRetryDelay"/> 起每次翻倍，封顶
/// <see cref="SettingConfigurationOptions.InitialLoadMaxRetryDelay"/>；首次读取成功（或宿主没有 Setting 表）时直接结束。
/// </remarks>
internal sealed class SettingConfigurationInitialLoadService(
    SettingConfigurationSource source,
    IOptionsMonitor<SettingConfigurationOptions> options,
    ILogger<SettingConfigurationInitialLoadService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = options.CurrentValue.InitialLoadRetryDelay;
        try
        {
            while (true)
            {
                var state = source.LoadState;
                if (state is SettingConfigurationLoadState.Loaded or SettingConfigurationLoadState.Unavailable)
                    return;

                await Task.Delay(delay, stoppingToken);

                // NotAttached / Pending：宿主还没初始化 SystemModule，或首次读取还在进行，没有要重试的东西。
                if (source.LoadState != SettingConfigurationLoadState.Failed)
                    continue;

                try
                {
                    await source.ReloadAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    var current = options.CurrentValue;
                    delay = Min(delay * 2, current.InitialLoadMaxRetryDelay);
                    logger.LogWarning(
                        "Settings still cannot be read from the database ({Attempts} failed attempt(s)); the app keeps running on the appsettings values. Next try in {Delay}. {Error}",
                        source.Provider?.FailedAttempts, delay, ex.Message);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 宿主停止。
        }
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
