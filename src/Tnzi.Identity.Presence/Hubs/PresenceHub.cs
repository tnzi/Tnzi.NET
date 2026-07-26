using Microsoft.AspNetCore.Authorization;

namespace Tnzi.Identity.Presence.Hubs;

/// <summary>
/// 通用在线状态实时 hub。服务端通过 <c>IMessagePushService&lt;PresenceHub&gt;</c> 向所有已认证连接
/// 广播 <c>Presence.Changed</c>（开放目录模型：presence 对任意登录用户可读，与 REST 端点一致）。
/// 客户端只订阅、不调用服务端方法（push-only）；需认证以便 <c>IConnectionManager</c> 按用户追踪连接。
/// 独立于 Chat：无 Chat 的应用也可用它实时看指定用户在线状态。
/// </summary>
[Authorize]
public class PresenceHub : TnziHub
{
    public PresenceHub(IConnectionManager connectionManager, IPermissionChecker? permissionChecker = null)
        : base(connectionManager, permissionChecker)
    {
    }
}
