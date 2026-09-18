using Microsoft.AspNetCore.Authorization;

namespace Tnzi.Chat.Hubs;

/// <summary>
/// Chat IM realtime hub. The server pushes "Chat.NewMessage" / "Chat.MessageRead" /
/// "Chat.ConversationChanged" to the per-user SignalR group (<c>HubGroupNames.ForUser</c>)
/// via IMessagePushService; the base hub joins every authenticated connection to that group,
/// so delivery is backplane-aware and does not depend on the in-process connection registry.
/// Clients do not invoke server methods (push-only hub); authentication is required
/// so the connection has a user id to be grouped under.
/// </summary>
[Authorize]
public class ChatHub : TnziHub
{
    /// <summary>
    /// Initializes a new instance of <see cref="ChatHub"/> with connection tracking.
    /// Single constructor so SignalR's hub activator always injects <see cref="IConnectionManager"/>
    /// (the registry feeds the admin online/connection views and presence; it is not on the push path).
    /// </summary>
    /// <param name="connectionManager">Connection manager for tracking user connections.</param>
    /// <param name="permissionChecker">Permission checker (optional).</param>
    public ChatHub(IConnectionManager connectionManager, IPermissionChecker? permissionChecker = null)
        : base(connectionManager, permissionChecker)
    {
    }
}
