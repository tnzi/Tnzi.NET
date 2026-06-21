using Microsoft.AspNetCore.Authorization;

namespace Tnzi.Chat.Hubs;

/// <summary>
/// Chat IM realtime hub. The server pushes "Chat.NewMessage" / "Chat.MessageRead" /
/// "Chat.ConversationChanged" to per-user connections via IMessagePushService.
/// Clients do not invoke server methods (push-only hub); authentication is required
/// so IConnectionManager tracks connections per authenticated user.
/// </summary>
[Authorize]
public class ChatHub : TnziHub
{
    /// <summary>
    /// Initializes a new instance of <see cref="ChatHub"/> with connection tracking.
    /// Single constructor so SignalR's hub activator always injects <see cref="IConnectionManager"/>
    /// (per-user tracking is required for <c>PushToUsersAsync</c> delivery).
    /// </summary>
    /// <param name="connectionManager">Connection manager for tracking user connections.</param>
    /// <param name="permissionChecker">Permission checker (optional).</param>
    public ChatHub(IConnectionManager connectionManager, IPermissionChecker? permissionChecker = null)
        : base(connectionManager, permissionChecker)
    {
    }
}
