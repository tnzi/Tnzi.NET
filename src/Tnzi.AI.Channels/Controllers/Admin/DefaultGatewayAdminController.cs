using Microsoft.AspNetCore.Mvc;
using Tnzi.AspNetCore.Models;
using Tnzi.AspNetCore.Mvc;

namespace Tnzi.AI.Channels.Controllers.Admin;

/// <summary>
/// Gateway 管理控制器 — 提供 Gateway 状态、连接和绑定规则的诊断端点
/// </summary>
[DefaultController]
[Route("admin/gateway")]
public class DefaultGatewayAdminController : ApiAdminControllerBase
{
    private readonly IGateway _gateway;
    private readonly IPresenceTracker _presence;
    private readonly IRepository<SessionBindingRule, Guid> _bindingRuleRepository;

    /// <summary>
    /// 初始化 Gateway 管理控制器
    /// </summary>
    public DefaultGatewayAdminController(
        IGateway gateway,
        IPresenceTracker presence,
        IRepository<SessionBindingRule, Guid> bindingRuleRepository)
    {
        _gateway = Check.NotNull(gateway);
        _presence = Check.NotNull(presence);
        _bindingRuleRepository = Check.NotNull(bindingRuleRepository);
    }

    /// <summary>
    /// 获取 Gateway 状态信息
    /// </summary>
    [HttpGet("status")]
    public virtual async Task<ApiResult<GatewayStatusDto>> GetStatus()
    {
        var connections = _presence.GetConnections();
        var sessions = await _gateway.GetSessionsAsync();

        var dto = new GatewayStatusDto
        {
            Enabled = true,
            ConnectedWebSocketCount = connections.Count,
            ActiveSessionCount = sessions.Count
        };

        return ApiResult<GatewayStatusDto>.Ok(dto);
    }

    /// <summary>
    /// 获取当前 WebSocket 连接列表
    /// </summary>
    [HttpGet("connections")]
    public virtual ApiResult<IReadOnlyList<GatewayConnectionInfo>> GetConnections()
    {
        var connections = _presence.GetConnections();
        return ApiResult<IReadOnlyList<GatewayConnectionInfo>>.Ok(connections);
    }

    /// <summary>
    /// 获取已配置的绑定规则列表
    /// </summary>
    [HttpGet("bindings")]
    public virtual async Task<ApiResult<IReadOnlyList<SessionBindingRule>>> GetBindings()
    {
        var rules = await _bindingRuleRepository.ToListAsync();
        return ApiResult<IReadOnlyList<SessionBindingRule>>.Ok(rules.AsReadOnly());
    }
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
