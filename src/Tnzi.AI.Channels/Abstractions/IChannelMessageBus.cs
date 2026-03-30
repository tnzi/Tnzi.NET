namespace Tnzi.AI.Channels.Abstractions;

/// <summary>
/// 消息总线 — 连接 Adapter（入站）和 Manager（处理+出站）
/// </summary>
public interface IChannelMessageBus
{
    /// <summary>发布入站消息（Adapter 调用）</summary>
    Task PublishInboundAsync(InboundMessage message, CancellationToken ct = default);

    /// <summary>消费入站消息（Manager 调用）</summary>
    Task<InboundMessage> ConsumeInboundAsync(CancellationToken ct = default);

    /// <summary>订阅出站消息（Adapter 调用）</summary>
    Task SubscribeOutboundAsync(Func<OutboundMessage, Task> handler);

    /// <summary>发布出站消息（Manager 调用）</summary>
    Task PublishOutboundAsync(OutboundMessage message, CancellationToken ct = default);
}
