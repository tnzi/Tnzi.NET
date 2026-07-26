namespace Tnzi.AI.Channels.Abstractions;

/// <summary>
/// 平台适配器接口 - 每个 IM 平台一个实现
/// </summary>
public interface IChannelAdapter : IAsyncDisposable
{
    /// <summary>适配器名称（telegram, feishu, dingtalk 等）</summary>
    string Name { get; }

    /// <summary>是否支持流式编辑消息</summary>
    bool SupportsStreaming { get; }

    /// <summary>
    /// 此渠道 Bot 实例归属的租户 ID（来自渠道 adapter options）。
    /// 多租户部署下用于将入站消息打上租户上下文（会话绑定规则分区、线程映射审计填充）。
    /// 默认 null = 单租户部署 / 渠道不归属任何租户（行为与既往完全一致）。
    /// </summary>
    Guid? TenantId => null;

    /// <summary>启动监听</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>停止监听</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>发送文本消息</summary>
    Task SendAsync(OutboundMessage message, CancellationToken ct = default);
}
