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

    /// <summary>
    /// 是否支持文件附件发送。
    /// 返回 false 的适配器不应被调用 <see cref="SendFileAsync"/>。
    /// 默认实现返回 false，有能力的平台（Telegram、Slack、Discord）覆盖为 true。
    /// </summary>
    bool SupportsFileAttachment => false;

    /// <summary>启动监听</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>停止监听</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>发送文本消息</summary>
    Task SendAsync(OutboundMessage message, CancellationToken ct = default);

    /// <summary>
    /// 发送文件附件。调用前应先检查 <see cref="SupportsFileAttachment"/>。
    /// 不支持文件的适配器应返回 false 而非抛出异常。
    /// </summary>
    Task<bool> SendFileAsync(OutboundMessage message, ResolvedAttachment attachment, CancellationToken ct = default);
}
