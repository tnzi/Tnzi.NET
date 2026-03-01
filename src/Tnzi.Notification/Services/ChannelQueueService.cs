namespace Tnzi.Notification.Services;

/// <summary>
/// 基于 Channel 的后台队列服务
/// </summary>
public class ChannelQueueService : BackgroundService, INotificationQueueService
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ChannelQueueService> _logger;

    public ChannelQueueService(
        IServiceProvider serviceProvider,
        ILogger<ChannelQueueService> logger,
        IOptions<NotificationOptions> options)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
        // 从配置读取队列容量，如果配置值无效（<=0）则使用默认值10000
        var queueCapacity = Check.NotNull(options).Value.Queue.QueueCapacity > 0
            ? options.Value.Queue.QueueCapacity
            : 10000;
        var channelOptions = new BoundedChannelOptions(queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        _queue = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(channelOptions);
    }

    public async Task EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
    {
        Check.NotNull(workItem);
        await _queue.Writer.WriteAsync(workItem);
    }

    public async Task EnqueueWithDelayAsync(Func<IServiceProvider, CancellationToken, Task> workItem, TimeSpan delay)
    {
        Check.NotNull(workItem);
        if (delay <= TimeSpan.Zero)
        {
            await EnqueueAsync(workItem);
            return;
        }

        // 包装为延迟执行的工作项
        await _queue.Writer.WriteAsync(async (sp, ct) =>
        {
            await Task.Delay(delay, ct);
            await workItem(sp, ct);
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notification Background Queue Service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var workItem = await _queue.Reader.ReadAsync(stoppingToken);

                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    await workItem(scope.ServiceProvider, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing notification background work item.");
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred processing notification queue.");
            }
        }

        _logger.LogInformation("Notification Background Queue Service is stopping.");
    }
}
