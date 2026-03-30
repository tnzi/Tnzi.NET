namespace Tnzi.AI.Channels.Abstractions;

/// <summary>
/// 消息调度管理器 — 消费入站消息，调用 AI，发布出站回复
/// </summary>
public interface IChannelManager
{
    /// <summary>启动调度循环</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>停止调度循环</summary>
    Task StopAsync(CancellationToken ct = default);
}
