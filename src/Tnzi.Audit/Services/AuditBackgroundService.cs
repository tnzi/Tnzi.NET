
namespace Tnzi.Audit.Services;

/// <summary>
/// 审计日志后台处理服务
/// </summary>
public class AuditBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuditBackgroundService> _logger;
    private readonly IAuditConsumer _auditConsumer;
    private readonly int _batchSize;

    public AuditBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<AuditBackgroundService> logger,
        IAuditConsumer auditConsumer,
        IOptions<Audit.Options.AuditOptions> options)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
        _auditConsumer = Check.NotNull(auditConsumer);
        _batchSize = Check.NotNull(options).Value.BatchSize;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Audit background service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 等待有数据可读
                if (await _auditConsumer.Reader.WaitToReadAsync(stoppingToken))
                {
                    await ProcessBatchAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // 忽略取消异常
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while processing audit log in background.");
            }
        }

        _logger.LogInformation("Audit background service is stopping.");
    }

    private async Task ProcessBatchAsync(CancellationToken stoppingToken)
    {
        var operations = new List<AuditOperation>();

        // 尝试读取一批数据
        while (operations.Count < _batchSize && _auditConsumer.Reader.TryRead(out var operation))
        {
            operations.Add(operation);
        }

        if (operations.Count == 0) return;

        using var scope = _serviceProvider.CreateScope();
        var auditStore = scope.ServiceProvider.GetRequiredService<IAuditStore>();

        try
        {
            await auditStore.SaveOperationBatchAsync(operations);
            _logger.LogDebug("Processed {Count} audit operations in background.", operations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save audit batch in background.");
        }
    }
}
