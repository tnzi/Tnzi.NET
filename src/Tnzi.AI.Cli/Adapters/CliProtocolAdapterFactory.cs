namespace Tnzi.AI.Cli.Adapters;

/// <summary>
/// 按协议族解析适配器。
/// </summary>
/// <remarks>
/// 走工厂而不是直接注入实例，因为适配器是<b>有状态且一次性</b>的（每次会话一个）。
/// 让 DI 直接注入会让两次并发运行共享同一份会话状态。
/// </remarks>
public class CliProtocolAdapterFactory : ICliProtocolAdapterFactory
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>初始化适配器工厂。</summary>
    public CliProtocolAdapterFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
    }

    /// <inheritdoc />
    public bool IsImplemented(CliAgentProtocol protocol) => protocol switch
    {
        CliAgentProtocol.StreamJson => true,
        CliAgentProtocol.Acp => true,
        // 厂商专有 app-server 的实现成本约等于其余全部之和，且需要持续跟随上游版本。
        // 描述表里保留 codex 是为了让管理端诚实展示「存在但本版本不支持」。
        CliAgentProtocol.VendorAppServer => false,
        _ => false
    };

    /// <inheritdoc />
    public ICliProtocolAdapter Create(CliAgentProtocol protocol) => protocol switch
    {
        CliAgentProtocol.StreamJson => new StreamJsonAdapter(
            _serviceProvider.GetRequiredService<ILogger<StreamJsonAdapter>>()),
        CliAgentProtocol.Acp => new AcpAdapter(
            _serviceProvider.GetRequiredService<ILogger<AcpAdapter>>()),
        _ => throw new CliProtocolNotImplementedException(protocol)
    };
}
