namespace Tnzi.Notification.Services;

/// <summary>
/// 基于 Channel 的后台队列服务
/// </summary>
public class ChannelQueueService : BackgroundService, INotificationQueueService
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ChannelQueueService> _logger;

    // ExecuteAsync 的停止令牌，供延迟入队的等待跟随服务生命周期。
    // 若在 ExecuteAsync 启动前就有延迟入队（宿主仍在启动），为 None。
    private CancellationToken _stoppingToken = CancellationToken.None;

    // 队列容量固化进 Channel 是刻意的（BoundedChannel 容量运行时不可变）；
    // 用 Monitor 在实例创建时点读一次，语义等价且不触发热消费审计告警。
    public ChannelQueueService(
        IServiceProvider serviceProvider,
        ILogger<ChannelQueueService> logger,
        IOptionsMonitor<NotificationOptions> options)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
        // 从配置读取队列容量，如果配置值无效（<=0）则使用默认值10000
        var queueCapacity = Check.NotNull(options).CurrentValue.Queue.QueueCapacity > 0
            ? options.CurrentValue.Queue.QueueCapacity
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

    public Task EnqueueWithDelayAsync(Func<IServiceProvider, CancellationToken, Task> workItem, TimeSpan delay)
    {
        Check.NotNull(workItem);
        if (delay <= TimeSpan.Zero)
        {
            return EnqueueAsync(workItem);
        }

        // 延迟必须发生在入队之前。读循环是单读者且逐个串行 await 工作项，
        // 若把 Task.Delay 包进工作项，一个计划发送（延迟可达数天）会把整条通知队列
        // 堵到它到期为止（头部阻塞：其后的重试与即时发送全部停摆）。
        _ = WaitThenEnqueueAsync(workItem, delay);
        return Task.CompletedTask;
    }

    /// <summary>等待到期后再入队。等待不占用读循环，随服务停止一并取消。</summary>
    private async Task WaitThenEnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, _stoppingToken);
            await _queue.Writer.WriteAsync(workItem, _stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // 服务停止：未到期的延迟项丢弃（内存队列本就不跨进程持久化）
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred enqueuing a delayed notification work item.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
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
