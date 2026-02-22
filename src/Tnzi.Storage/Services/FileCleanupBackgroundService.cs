namespace Tnzi.Storage.Services;

/// <summary>
/// 文件清理后台服务
/// 定时执行文件清理任务
/// </summary>
public class FileCleanupBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly StorageOptions _options;
    private readonly ILogger<FileCleanupBackgroundService> _logger;

    public FileCleanupBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<StorageOptions> options,
        ILogger<FileCleanupBackgroundService> logger)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Cleanup.Enabled)
        {
            _logger.LogInformation("File cleanup background task is disabled");
            return;
        }

        _logger.LogInformation("File cleanup background task started, interval: {Interval} minutes",
            _options.Cleanup.IntervalMinutes);

        // 启动后等待一段时间再执行首次清理（避免启动时资源竞争）
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
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

            // 等待下一次执行
            var interval = TimeSpan.FromMinutes(_options.Cleanup.IntervalMinutes);
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
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
