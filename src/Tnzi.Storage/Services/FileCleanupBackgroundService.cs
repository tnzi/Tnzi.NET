namespace Tnzi.Storage.Services;

/// <summary>
/// 文件清理后台服务
/// 定时执行文件清理任务
/// </summary>
/// <remarks>
/// ★★ <b>这个可选的后台任务无论出什么事都不能把宿主带走。</b>.NET 的 <c>BackgroundService</c>
/// 在 <c>ExecuteAsync</c> 故障时默认停止整个宿主（<c>BackgroundServiceExceptionBehavior.StopHost</c>），
/// 而此前这里有两条受支持的配置会走到那一步：间隔超过约 71582 分钟，或一条首次触发在约 49.7 天之后的
/// 合法 cron（季度、年度）。<c>Task.Delay</c> 不接受超过 Timer 上限的时长，抛出的
/// <c>ArgumentOutOfRangeException</c> 只被当成「不是取消」放过去。所以等待一律按
/// <see cref="MaxDelaySlice"/> 切片，并且整个调度外面有一层兜底：失败关闭的是清理任务，不是应用。
/// </remarks>
public class FileCleanupBackgroundService : BackgroundService
{
    /// <summary>
    /// 单次 <c>Task.Delay</c> 的上限。更长的等待切成多段，每段之后按目标时刻重算剩余时长。
    /// </summary>
    internal static readonly TimeSpan MaxDelaySlice = TimeSpan.FromDays(1);

    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<StorageOptions> _options;
    private readonly ILogger<FileCleanupBackgroundService> _logger;

    public FileCleanupBackgroundService(
        IServiceProvider serviceProvider,
        IOptionsMonitor<StorageOptions> options,
        ILogger<FileCleanupBackgroundService> logger)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.Cleanup.Enabled)
        {
            _logger.LogInformation("File cleanup background task is disabled");
            return;
        }

        try
        {
            await RunScheduleAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 宿主正在停止
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "File cleanup background task stopped after an unexpected scheduling error. The application keeps running; scheduled cleanup resumes after a restart.");
        }
    }

    /// <summary>
    /// 等到 <paramref name="targetUtc"/>。按 <see cref="MaxDelaySlice"/> 切片，所以任意远的目标都不会让
    /// <c>Task.Delay</c> 越界抛出；每段之后按目标时刻重算，系统时钟被校正时也不会多等一整段。
    /// </summary>
    internal static async Task DelayUntilAsync(DateTime targetUtc, CancellationToken cancellationToken)
    {
        while (true)
        {
            var remaining = targetUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return;

            await Task.Delay(remaining < MaxDelaySlice ? remaining : MaxDelaySlice, cancellationToken);
        }
    }

    /// <summary>
    /// 本轮之后等多久。小于 1 分钟的配置值按 1 分钟算：0 会变成无间隔的紧循环清理，负数会让等待本身抛出。
    /// 设置中心已经把下限钉在 1，这里兜的是直接写在配置文件里的值。
    /// </summary>
    internal static TimeSpan EffectiveInterval(int configuredMinutes)
        => TimeSpan.FromMinutes(Math.Max(1, configuredMinutes));

    private async Task RunScheduleAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("File cleanup background task started, interval: {Interval} minutes",
            _options.CurrentValue.Cleanup.IntervalMinutes);

        // 解析 Cron 表达式（设置则优先于 IntervalMinutes）；非法表达式回退到固定间隔。
        Cronos.CronExpression? cron = null;
        var cronExpr = _options.CurrentValue.Cleanup.CronExpression;
        if (!string.IsNullOrWhiteSpace(cronExpr))
        {
            try
            {
                cron = Cronos.CronExpression.Parse(cronExpr);
                _logger.LogInformation("File cleanup scheduled by cron expression: {Cron}", cronExpr);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Invalid Cleanup.CronExpression '{CronExpression}', falling back to IntervalMinutes ({Interval} min).",
                    cronExpr, _options.CurrentValue.Cleanup.IntervalMinutes);
            }
        }

        // 间隔模式：启动后等待一段时间再首次执行（避免启动时资源竞争）。
        // Cron 模式：直接进入循环，由 Cron 决定首次执行时间。
        if (cron == null)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            // Cron 模式：等待到下一次计划执行时间
            if (cron != null)
            {
                var next = cron.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc);
                if (next == null)
                {
                    _logger.LogWarning(
                        "Cron expression '{CronExpression}' has no further occurrences, stopping cleanup schedule",
                        cronExpr);
                    break;
                }

                try
                {
                    await DelayUntilAsync(next.Value, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            try
            {
                await ExecuteCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // 正常取消，忽略
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "File cleanup task encountered an exception");
            }

            // 间隔模式：等待固定间隔后再次执行（Cron 模式由循环顶部重新计算）
            if (cron == null)
            {
                var configuredMinutes = _options.CurrentValue.Cleanup.IntervalMinutes;
                if (configuredMinutes < 1)
                {
                    _logger.LogWarning(
                        "Storage:Cleanup:IntervalMinutes is {Configured}; cleanup runs every minute instead. Set it to 1 or more.",
                        configuredMinutes);
                }

                try
                {
                    await DelayUntilAsync(DateTime.UtcNow + EffectiveInterval(configuredMinutes), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("File cleanup background task stopped");
    }

    /// <summary>
    /// 执行清理任务
    /// </summary>
    private async Task ExecuteCleanupAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Start scheduled file cleanup");

        // 使用 Scope 获取 Scoped 服务
        using var scope = _serviceProvider.CreateScope();
        var cleanupService = scope.ServiceProvider.GetService<IFileCleanupService>();

        if (cleanupService == null)
        {
            _logger.LogWarning("Unable to resolve IFileCleanupService service");
            return;
        }

        var result = await cleanupService.CleanupAsync(cancellationToken);

        if (result.Success)
        {
            if (result.TotalDeleted > 0)
            {
                _logger.LogInformation(
                    "Scheduled cleanup completed: TempFiles={TempFiles}, OrphanFiles={OrphanFiles}, OrphanRefs={OrphanRefs}",
                    result.TemporaryFilesDeleted,
                    result.OrphanFilesDeleted,
                    result.OrphanReferencesDeleted);
            }
            else
            {
                _logger.LogDebug("Scheduled cleanup completed, no files to clean");
            }
        }
        else
        {
            _logger.LogWarning("Scheduled cleanup failed: {Errors}",
                string.Join("; ", result.Errors));
        }
    }
}
