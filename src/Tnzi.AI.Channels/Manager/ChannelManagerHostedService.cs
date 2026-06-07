namespace Tnzi.AI.Channels.Manager;

/// <summary>
/// 将 ChannelManager 和所有 ChannelAdapter 的生命周期绑定到 ASP.NET Core Host
/// </summary>
public class ChannelManagerHostedService : IHostedService
{
    private readonly IChannelManager _manager;
    private readonly IEnumerable<IChannelAdapter> _adapters;
    private readonly IChannelMessageBus _bus;
    private readonly ILogger<ChannelManagerHostedService> _logger;

    public ChannelManagerHostedService(
        IChannelManager manager,
        IEnumerable<IChannelAdapter> adapters,
        IChannelMessageBus bus,
        ILogger<ChannelManagerHostedService> logger)
    {
        _manager = Check.NotNull(manager);
        _adapters = Check.NotNull(adapters);
        _bus = Check.NotNull(bus);
        _logger = Check.NotNull(logger);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        // 订阅出站 — 将回复路由到对应 Adapter
        var adapterMap = _adapters.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
        await _bus.SubscribeOutboundAsync(async outbound =>
        {
            if (adapterMap.TryGetValue(outbound.ChannelName, out var adapter))
            {
                await adapter.SendAsync(outbound, CancellationToken.None);

                // 发送附件（仅支持文件的适配器）
                if (outbound.Attachments is { Count: > 0 } && adapter.SupportsFileAttachment)
                {
                    foreach (var attachment in outbound.Attachments)
                    {
                        await adapter.SendFileAsync(outbound, attachment, CancellationToken.None);
                    }
                }
            }
            else
            {
                _logger.LogWarning("No adapter registered for channel: {Channel}", outbound.ChannelName);
            }
        });

        // 启动所有适配器
        foreach (var adapter in _adapters)
        {
            await adapter.StartAsync(ct);
            _logger.LogInformation("Channel adapter started: {AdapterName}", adapter.Name);
        }

        // 启动调度管理器
        await _manager.StartAsync(ct);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _manager.StopAsync(ct);

        foreach (var adapter in _adapters)
        {
            await adapter.StopAsync(ct);
            await adapter.DisposeAsync();
        }
    }
}
