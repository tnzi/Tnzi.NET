using Tnzi.AI.Channels.Bus;
using Tnzi.AI.Channels.Models;

namespace Tnzi.Tests.AI.Channels;

public class InMemoryChannelMessageBusTests
{
    private readonly InMemoryChannelMessageBus _bus = new();

    [Fact]
    public async Task PublishInbound_ConsumeInbound_ReturnsMessage()
    {
        var message = new InboundMessage("telegram", "123", "user1", "Hello");
        await _bus.PublishInboundAsync(message);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var consumed = await _bus.ConsumeInboundAsync(cts.Token);
        Assert.Equal("Hello", consumed.Text);
        Assert.Equal("telegram", consumed.ChannelName);
    }

    [Fact]
    public async Task ConsumeInbound_EmptyQueue_BlocksUntilAvailable()
    {
        var consumeTask = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            return await _bus.ConsumeInboundAsync(cts.Token);
        });

        await Task.Delay(100);
        Assert.False(consumeTask.IsCompleted);

        var message = new InboundMessage("telegram", "123", "user1", "Delayed");
        await _bus.PublishInboundAsync(message);

        var result = await consumeTask;
        Assert.Equal("Delayed", result.Text);
    }

    [Fact]
    public async Task PublishOutbound_SubscriberReceivesMessage()
    {
        OutboundMessage? received = null;
        await _bus.SubscribeOutboundAsync(msg =>
        {
            received = msg;
            return Task.CompletedTask;
        });

        var outbound = new OutboundMessage("telegram", "123", Guid.NewGuid(), "Reply");
        await _bus.PublishOutboundAsync(outbound);

        // 允许异步处理时间
        await Task.Delay(100);
        Assert.NotNull(received);
        Assert.Equal("Reply", received!.Text);
    }

    [Fact]
    public async Task PublishOutbound_MultipleSubscribers_AllReceive()
    {
        var received1 = new List<OutboundMessage>();
        var received2 = new List<OutboundMessage>();

        await _bus.SubscribeOutboundAsync(msg => { received1.Add(msg); return Task.CompletedTask; });
        await _bus.SubscribeOutboundAsync(msg => { received2.Add(msg); return Task.CompletedTask; });

        var outbound = new OutboundMessage("telegram", "123", Guid.NewGuid(), "Broadcast");
        await _bus.PublishOutboundAsync(outbound);

        await Task.Delay(100);
        Assert.Single(received1);
        Assert.Single(received2);
    }

    [Fact]
    public async Task PublishInbound_FIFO_Order()
    {
        await _bus.PublishInboundAsync(new InboundMessage("telegram", "1", "u", "First"));
        await _bus.PublishInboundAsync(new InboundMessage("telegram", "1", "u", "Second"));
        await _bus.PublishInboundAsync(new InboundMessage("telegram", "1", "u", "Third"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var m1 = await _bus.ConsumeInboundAsync(cts.Token);
        var m2 = await _bus.ConsumeInboundAsync(cts.Token);
        var m3 = await _bus.ConsumeInboundAsync(cts.Token);

        Assert.Equal("First", m1.Text);
        Assert.Equal("Second", m2.Text);
        Assert.Equal("Third", m3.Text);
    }
}
