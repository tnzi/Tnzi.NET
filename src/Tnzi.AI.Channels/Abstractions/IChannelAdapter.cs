namespace Tnzi.AI.Channels.Abstractions;

/// <summary>
/// 平台适配器接口 — 每个 IM 平台一个实现
/// </summary>
public interface IChannelAdapter : IAsyncDisposable
{
    /// <summary>适配器名称（telegram, feishu, dingtalk 等）</summary>
    string Name { get; }

    /// <summary>是否支持流式编辑消息</summary>
    bool SupportsStreaming { get; }

    /// <summary>启动监听</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>停止监听</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>发送文本消息</summary>
    Task SendAsync(OutboundMessage message, CancellationToken ct = default);

    /// <summary>发送文件附件</summary>
    Task<bool> SendFileAsync(OutboundMessage message, ResolvedAttachment attachment, CancellationToken ct = default);
}
