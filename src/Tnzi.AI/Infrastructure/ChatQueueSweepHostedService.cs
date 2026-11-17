namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 线程消息队列的定时扫描：补上漏掉的运行结束事件（服务在一轮结束前后重启）、本该当场发出却没发完的消息，
/// 并扣住发出途中卡住的消息。间隔与阈值见 <c>AI:Chat:Queue</c>。
/// </summary>
/// <remarks>每轮重读间隔；一轮出错只记日志，下一轮照常。</remarks>
public class ChatQueueSweepHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AiChatOptions> _options;
    private readonly ILogger<ChatQueueSweepHostedService> _logger;

    public ChatQueueSweepHostedService(
        IServiceScopeFactory scopeFactory, IOptionsMonitor<AiChatOptions> options, ILogger<ChatQueueSweepHostedService> logger)
    {
        _scopeFactory = Check.NotNull(scopeFactory);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_options.CurrentValue.Queue.SweepInterval, stoppingToken);
                await SweepOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 关停，正常路径
        }
    }

    private async Task SweepOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var delivery = scope.ServiceProvider.GetRequiredService<IChatQueueDelivery>();
            await delivery.SweepAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat queue sweep failed; it runs again at the next interval.");
        }
    }
}
