using Microsoft.AspNetCore.Mvc;
using Tnzi.AspNetCore.Models;
using Tnzi.AspNetCore.Mvc;

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

    /// <summary>
    /// 初始化 Gateway 管理控制器（所有依赖可选，Gateway 未启用时返回 disabled 状态）
    /// </summary>
    public DefaultGatewayAdminController(
        IGateway? gateway = null,
        IPresenceTracker? presence = null,
        IRepository<SessionBindingRule, Guid>? bindingRuleRepository = null)
    {
        _gateway = gateway;
        _presence = presence;
        _bindingRuleRepository = bindingRuleRepository;
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
        if (_presence == null)
        {
            return ApiResult<IReadOnlyList<GatewayConnectionInfo>>.Ok(Array.Empty<GatewayConnectionInfo>());
        }

        var connections = _presence.GetConnections();
        return ApiResult<IReadOnlyList<GatewayConnectionInfo>>.Ok(connections);
    }

    /// <summary>
    /// 获取已配置的绑定规则列表
    /// </summary>
    [HttpGet("bindings")]
    public virtual async Task<ApiResult<IReadOnlyList<SessionBindingRule>>> GetBindings()
    {
        if (_bindingRuleRepository == null)
        {
            return ApiResult<IReadOnlyList<SessionBindingRule>>.Ok(Array.Empty<SessionBindingRule>());
        }

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
