namespace Tnzi.AI.Channels.Bus;

/// <summary>
/// 进程内消息总线 — 使用 System.Threading.Channels 实现 FIFO 队列
/// </summary>
public class InMemoryChannelMessageBus : IChannelMessageBus
{
    private readonly Channel<InboundMessage> _inbound = Channel.CreateUnbounded<InboundMessage>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    private readonly List<Func<OutboundMessage, Task>> _outboundSubscribers = [];
    private readonly object _subscriberLock = new();
    private readonly ILogger<InMemoryChannelMessageBus> _logger;

    public InMemoryChannelMessageBus(ILogger<InMemoryChannelMessageBus> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task PublishInboundAsync(InboundMessage message, CancellationToken ct = default)
    {
        Check.NotNull(message);
        await _inbound.Writer.WriteAsync(message, ct);
    }

    public async Task<InboundMessage> ConsumeInboundAsync(CancellationToken ct = default)
    {
        return await _inbound.Reader.ReadAsync(ct);
    }

    public Task SubscribeOutboundAsync(Func<OutboundMessage, Task> handler)
    {
        Check.NotNull(handler);
        lock (_subscriberLock)
        {
            _outboundSubscribers.Add(handler);
        }
        return Task.CompletedTask;
    }

    public async Task PublishOutboundAsync(OutboundMessage message, CancellationToken ct = default)
    {
        Check.NotNull(message);
        Func<OutboundMessage, Task>[] subscribers;
        lock (_subscriberLock)
        {
            subscribers = [.. _outboundSubscribers];
        }

        foreach (var subscriber in subscribers)
        {
            try
            {
                await subscriber(message);
            }
            catch (Exception ex)
            {
                // 单个 subscriber 异常不应中断其他 subscriber 的执行
                _logger.LogWarning(ex, "Outbound message subscriber failed for channel '{Channel}'", message.ChannelName);
            }
        }
    }
}
