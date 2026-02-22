
namespace Tnzi.Identity.Services;

/// <summary>
/// 登录日志后台处理服务
/// </summary>
public class LoginLogBackgroundService : BackgroundService
{
    private static readonly TimeSpan ErrorRetryDelay = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;

    private readonly ILoginLogConsumer _consumer;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LoginLogBackgroundService> _logger;

    public LoginLogBackgroundService(
        ILoginLogConsumer consumer,
        IServiceProvider serviceProvider,
        ILogger<LoginLogBackgroundService> logger)
    {
        _consumer = Check.NotNull(consumer);
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await _consumer.Reader.WaitToReadAsync(stoppingToken))
                {
                    await ProcessBatchAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // 服务停止时 WaitToReadAsync(stoppingToken) 会抛出，属预期行为，忽略即可
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in login log background service.");
                await Task.Delay(ErrorRetryDelay, stoppingToken);
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken stoppingToken)
    {
        var logs = new List<LoginLog>();
        while (logs.Count < BatchSize && _consumer.Reader.TryRead(out var log))
        {
            logs.Add(log);
        }

        if (logs.Count == 0) return;

        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<LoginLog, Guid>>();

        try
        {
            // 确保设置创建时间
            foreach (var log in logs)
            {
                if (log.CreationTime == default)
                    log.CreationTime = DateTime.UtcNow;
            }

            await repository.InsertManyAsync(logs);
            _logger.LogDebug("Saved {Count} login logs to database.", logs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save login logs to database.");
        }
    }
}