namespace Tnzi.Storage.Services;

/// <summary>
/// 文件清理后台服务
/// 定时执行文件清理任务
/// </summary>
public class FileCleanupBackgroundService : BackgroundService
{
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

                var wait = next.Value - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(wait, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
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
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(_options.CurrentValue.Cleanup.IntervalMinutes), stoppingToken);
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
