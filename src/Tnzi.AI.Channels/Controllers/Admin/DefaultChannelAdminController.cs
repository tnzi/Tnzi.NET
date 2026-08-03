
namespace Tnzi.AI.Channels.Controllers.Admin;

/// <summary>
/// Channel 适配器管理控制器 - 查询适配器状态和频道配置
/// </summary>
[DefaultController]
[Route("admin/channels")]
[ApiAuthorize(PermissionName = "ai.channels.view")]
public class DefaultChannelAdminController : ApiAdminControllerBase
{
    private readonly IEnumerable<IChannelAdapter>? _adapters;
    private readonly IChannelMessageBus? _bus;
    private readonly ChannelsModuleOptions _options;
    private readonly GatewayOptions _gatewayOptions;

    /// <summary>
    /// 初始化 Channel 管理控制器（适配器/消息总线可选，模块未启用时返回 disabled 状态）
    /// </summary>
    public DefaultChannelAdminController(
        IOptions<ChannelsModuleOptions> options,
        IOptions<GatewayOptions> gatewayOptions,
        IEnumerable<IChannelAdapter>? adapters = null,
        IChannelMessageBus? bus = null)
    {
        _options = Check.NotNull(options).Value;
        _gatewayOptions = Check.NotNull(gatewayOptions).Value;
        _adapters = adapters;
        _bus = bus;
    }

    /// <summary>
    /// 获取 Channels 模块状态
    /// </summary>
    [HttpGet("status")]
    public virtual ApiResult<ChannelModuleStatusDto> GetStatus()
    {
        var dto = new ChannelModuleStatusDto
        {
            Enabled = _options.Enabled,
            MaxConcurrency = _options.MaxConcurrency,
            StreamingThrottleMs = _gatewayOptions.StreamingThrottleMs,
            Adapters = (_adapters ?? []).Select(a => new ChannelAdapterDto
            {
                Name = a.Name,
                SupportsStreaming = a.SupportsStreaming
            }).ToList().AsReadOnly()
        };

        return ApiResult<ChannelModuleStatusDto>.Ok(dto);
    }

    /// <summary>
    /// 获取已注册的适配器列表
    /// </summary>
    [HttpGet("adapters")]
    public virtual ApiResult<IReadOnlyList<ChannelAdapterDto>> GetAdapters()
    {
        var adapters = (_adapters ?? []).Select(a => new ChannelAdapterDto
        {
            Name = a.Name,
            SupportsStreaming = a.SupportsStreaming
        }).ToList().AsReadOnly();

        return ApiResult<IReadOnlyList<ChannelAdapterDto>>.Ok(adapters);
    }
}

/// <summary>
/// Channel 模块状态 DTO
/// </summary>
public class ChannelModuleStatusDto
{
    /// <summary>模块是否启用</summary>
    public bool Enabled { get; init; }

    /// <summary>最大并发处理数</summary>
    public int MaxConcurrency { get; init; }

    /// <summary>流式节流间隔（毫秒）</summary>
    public int StreamingThrottleMs { get; init; }

    /// <summary>已注册的适配器列表</summary>
    public IReadOnlyList<ChannelAdapterDto> Adapters { get; init; } = [];
}

/// <summary>
/// Channel 适配器 DTO
/// </summary>
public class ChannelAdapterDto
{
    /// <summary>适配器名称</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>是否支持流式消息</summary>
    public bool SupportsStreaming { get; init; }
}
