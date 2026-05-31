using Microsoft.AspNetCore.Mvc;
using Tnzi.AspNetCore.Models;
using Tnzi.AspNetCore.Mvc;

namespace Tnzi.AI.Device.Controllers.Admin;

/// <summary>
/// Device 管理控制器 — 提供设备列表、配对审批和设备撤销端点
/// </summary>
[DefaultController]
[Route("admin/devices")]
public class DefaultDeviceAdminController : ApiAdminControllerBase
{
    private readonly IDeviceRegistry _registry;
    private readonly IDevicePairingService _pairingService;

    /// <summary>
    /// 初始化 Device 管理控制器
    /// </summary>
    public DefaultDeviceAdminController(IDeviceRegistry registry, IDevicePairingService pairingService)
    {
        _registry = Check.NotNull(registry);
        _pairingService = Check.NotNull(pairingService);
    }

    /// <summary>
    /// 获取已连接设备列表
    /// </summary>
    [HttpGet]
    public virtual ApiResult<IReadOnlyList<DeviceNodeDto>> GetDevices()
    {
        var nodes = _registry.GetConnectedNodes();
        var dtos = nodes.Select(n => new DeviceNodeDto
        {
            NodeId = n.NodeId,
            Name = n.Name,
            Platform = n.Platform,
            State = n.State,
            Capabilities = n.Capabilities.Select(c => c.Family).ToList()
        }).ToList().AsReadOnly();

        return ApiResult<IReadOnlyList<DeviceNodeDto>>.Ok(dtos);
    }

    /// <summary>
    /// 获取待审批的配对请求 — Code 已从响应中移除
    /// </summary>
    [HttpGet("pairing")]
    public virtual async Task<ApiResult<IReadOnlyList<PendingPairingRequestDto>>> GetPendingPairings(CancellationToken cancellationToken = default)
    {
        var requests = await _pairingService.GetPendingRequestsAsync(cancellationToken);
        return ApiResult<IReadOnlyList<PendingPairingRequestDto>>.Ok(requests);
    }

    /// <summary>
    /// 批准配对请求（通过公开的 requestId，而非秘密 Code）
    /// </summary>
    [HttpPost("pairing/{requestId}/approve")]
    public virtual async Task<ApiResult<DevicePairingInfo>> ApprovePairing(string requestId, CancellationToken cancellationToken = default)
    {
        var result = await _pairingService.ApprovePairingByIdAsync(requestId, cancellationToken);
        return result != null
            ? ApiResult<DevicePairingInfo>.Ok(result)
            : ApiResult<DevicePairingInfo>.Error("Pairing request not found or expired.", 404);
    }

    /// <summary>
    /// 拒绝配对请求（通过公开的 requestId，而非秘密 Code）
    /// </summary>
    [HttpPost("pairing/{requestId}/reject")]
    public virtual async Task<ApiResult> RejectPairing(string requestId, CancellationToken cancellationToken = default)
    {
        var success = await _pairingService.RejectPairingByIdAsync(requestId, cancellationToken);
        return success
            ? ApiResult.Ok()
            : ApiResult.Error("Pairing request not found or expired.", 404);
    }

    /// <summary>
    /// 撤销设备
    /// </summary>
    [HttpDelete("{nodeId}")]
    public virtual async Task<ApiResult> RevokeDevice(string nodeId, CancellationToken cancellationToken = default)
    {
        await _pairingService.RevokeDeviceAsync(nodeId, cancellationToken);
        _registry.Unregister(nodeId);
        return ApiResult.Ok();
    }
}
