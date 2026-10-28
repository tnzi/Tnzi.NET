
namespace Tnzi.AI.Channels.Controllers.Admin;

/// <summary>
/// Gateway 管理控制器 - 提供 Gateway 状态、连接和绑定规则的诊断端点
/// </summary>
[DefaultController]
[Route("admin/gateway")]
[ApiAuthorize(PermissionName = "ai.channels.view")]
public class DefaultGatewayAdminController : ApiAdminControllerBase
{
    private readonly IGateway? _gateway;
    private readonly IPresenceTracker? _presence;
    private readonly IRepository<SessionBindingRule, Guid>? _bindingRuleRepository;
    private readonly IReadOnlyList<SessionBindingRule> _configBindingRules;
    private readonly ICurrentTenant? _currentTenant;
    private readonly bool _multiTenancyEnabled;

    /// <summary>
    /// 初始化 Gateway 管理控制器（所有依赖可选，Gateway 未启用时返回 disabled 状态）
    /// </summary>
    public DefaultGatewayAdminController(
        IGateway? gateway = null,
        IPresenceTracker? presence = null,
        IRepository<SessionBindingRule, Guid>? bindingRuleRepository = null,
        ICurrentTenant? currentTenant = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null,
        IReadOnlyList<SessionBindingRule>? configBindingRules = null)
    {
        _gateway = gateway;
        _presence = presence;
        _bindingRuleRepository = bindingRuleRepository;
        _configBindingRules = configBindingRules ?? [];
        _currentTenant = currentTenant;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
    }

    /// <summary>
    /// 获取 Gateway 状态信息
    /// </summary>
    [HttpGet("status")]
    public virtual async Task<ApiResult<GatewayStatusDto>> GetStatus()
    {
        if (_gateway == null || _presence == null)
        {
            return ApiResult<GatewayStatusDto>.Ok(new GatewayStatusDto { Enabled = false });
        }

        var connections = _presence.GetConnections().Count(c => IsVisible(c.TenantId));
        var sessions = (await _gateway.GetSessionsAsync()).Count(s => IsVisible(s.TenantId));

        var dto = new GatewayStatusDto
        {
            Enabled = true,
            ConnectedWebSocketCount = connections,
            ActiveSessionCount = sessions
        };

        return ApiResult<GatewayStatusDto>.Ok(dto);
    }

    /// <summary>
    /// 获取当前 WebSocket 连接列表
    /// </summary>
    [HttpGet("connections")]
    public virtual ApiResult<IReadOnlyList<GatewayConnectionInfo>> GetConnections()
    {
        if (_presence == null)
        {
            return ApiResult<IReadOnlyList<GatewayConnectionInfo>>.Ok(Array.Empty<GatewayConnectionInfo>());
        }

        var connections = _presence.GetConnections()
            .Where(c => IsVisible(c.TenantId))
            .ToList()
            .AsReadOnly();
        return ApiResult<IReadOnlyList<GatewayConnectionInfo>>.Ok(connections);
    }

    /// <summary>
    /// 一条在线连接 / 活跃会话对当前调用者是否可见。
    /// </summary>
    /// <remarks>
    /// 连接与会话都存在进程内单例里、不经过任何仓储，全局租户过滤器管不到它们，所以在这里按租户筛。
    /// 口径与全局过滤器相同：多租户开启时严格等值（宿主上下文只看到未归属租户的条目，没有「宿主看全部」的特例），
    /// 与同一控制器下走仓储的 <c>/bindings</c> 一致；多租户关闭时不筛。
    /// 多租户开启却解析不到当前租户服务时一条都不给（失败关闭）。
    /// </remarks>
    protected virtual bool IsVisible(Guid? entryTenantId)
    {
        if (!_multiTenancyEnabled)
        {
            return true;
        }

        return _currentTenant != null && entryTenantId == _currentTenant.Id;
    }

    /// <summary>
    /// 获取绑定规则列表：配置规则（<c>AI:Channels:Gateway:BindingRules</c>）与数据库规则合在一起，
    /// 与 <see cref="DefaultSessionBinder"/> 匹配时用的是同一组、同一顺序（优先级降序，同优先级配置在前）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 只列数据库规则时，真正在路由流量的配置规则在这里一条都看不见：运维看到「0 条绑定」会以为规则没加载，
    /// 而框架本身并没有创建数据库规则的入口。<see cref="SessionBindingRuleDto.Source"/> 标出每条的来源。
    /// </para>
    /// <para>
    /// 投影成 DTO 而不是直出实体：实体带审计列与 <c>TenantId</c>，把它们发到管理端既泄漏
    /// 内部结构，也让实体字段变成事实上的 API 契约（以后加一列就等于改了线缆形态）。
    /// 数据库规则经仓储读取，受全局租户过滤器约束；配置规则是部署级全局规则，对任何租户都生效，因此都列出。
    /// </para>
    /// </remarks>
    [HttpGet("bindings")]
    public virtual async Task<ApiResult<IReadOnlyList<SessionBindingRuleDto>>> GetBindings()
    {
        var databaseRules = _bindingRuleRepository == null
            ? []
            : await _bindingRuleRepository.ToListAsync();

        // OrderByDescending 是稳定排序：同优先级下保持「配置在前」，与绑定器的合并顺序一致
        var rules = _configBindingRules.Select(r => ToDto(r, SessionBindingRuleSources.Config))
            .Concat(databaseRules.Select(r => ToDto(r, SessionBindingRuleSources.Database)))
            .OrderByDescending(r => r.Priority)
            .ToList();

        return ApiResult<IReadOnlyList<SessionBindingRuleDto>>.Ok(rules.AsReadOnly());
    }

    private static SessionBindingRuleDto ToDto(SessionBindingRule rule, string source) => new()
    {
        Id = rule.Id,
        Channel = rule.Channel,
        PeerKind = rule.PeerKind,
        PeerId = rule.PeerId,
        AgentId = rule.AgentId,
        Scope = rule.Scope,
        Priority = rule.Priority,
        IsEnabled = rule.IsEnabled,
        Source = source
    };
}

/// <summary>
/// Gateway 状态 DTO
/// </summary>
public class GatewayStatusDto
{
    /// <summary>Gateway 是否已启用</summary>
    public bool Enabled { get; init; }

    /// <summary>当前 WebSocket 连接数</summary>
    public int ConnectedWebSocketCount { get; init; }

    /// <summary>活跃会话数</summary>
    public int ActiveSessionCount { get; init; }
}
