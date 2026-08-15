
namespace Tnzi.SignalR.Hubs;

/// <summary>
/// Tnzi SignalR Hub 基类 (强类型版本)
/// </summary>
/// <typeparam name="TClient">客户端接口类型</typeparam>
public abstract class TnziHub<TClient> : Hub<TClient> where TClient : class
{
    private readonly IConnectionManager? _connectionManager;
    private readonly IPermissionChecker? _permissionChecker;

    /// <summary>
    /// 初始化一个<see cref="TnziHub{TClient}"/>类型的新实例
    /// </summary>
    protected TnziHub()
    {
    }

    /// <summary>
    /// 初始化一个<see cref="TnziHub{TClient}"/>类型的新实例
    /// </summary>
    /// <param name="connectionManager">连接管理器</param>
    /// <param name="permissionChecker">权限检查器 (可选)</param>
    protected TnziHub(IConnectionManager connectionManager, IPermissionChecker? permissionChecker = null)
    {
        _connectionManager = Check.NotNull(connectionManager);
        _permissionChecker = permissionChecker;
    }

    /// <summary>
    /// 获取当前用户ID
    /// </summary>
    protected Guid? CurrentUserId
    {
        get
        {
            var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirst("sub");
            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
            {
                return userId;
            }
            return null;
        }
    }

    /// <summary>
    /// 获取当前用户名
    /// </summary>
    protected string? CurrentUserName => Context.User?.Identity?.Name;

    /// <summary>
    /// 检查当前用户是否已认证
    /// </summary>
    protected bool IsAuthenticated => Context.User?.Identity?.IsAuthenticated ?? false;

    /// <summary>
    /// 检查当前用户是否有指定权限 (复用框架权限检查器)
    /// </summary>
    /// <param name="permissionName">权限名称</param>
    /// <returns>是否有权限</returns>
    protected async Task<bool> HasPermissionAsync(string permissionName)
    {
        Check.NotNullOrWhiteSpace(permissionName);
        if (_permissionChecker == null)
            return false;
        return await _permissionChecker.IsGrantedAsync(permissionName);
    }

    /// <summary>
    /// 检查当前用户是否在指定角色中
    /// </summary>
    /// <param name="role">角色名称</param>
    /// <returns>是否在角色中</returns>
    protected bool IsInRole(string role)
    {
        return Context.User?.IsInRole(role) ?? false;
    }

    /// <summary>
    /// 要求当前用户已认证，否则抛出异常
    /// </summary>
    protected void RequireAuthentication()
    {
        if (!IsAuthenticated)
        {
            throw new HubException("User is not authenticated");
        }
    }

    /// <summary>
    /// 要求当前用户有指定权限，否则抛出异常
    /// </summary>
    /// <param name="permissionName">权限名称</param>
    protected async Task RequirePermissionAsync(string permissionName)
    {
        Check.NotNullOrWhiteSpace(permissionName);
        RequireAuthentication();
        if (_permissionChecker == null)
        {
            throw new HubException("Permission checker not available");
        }

        var isGranted = await _permissionChecker.IsGrantedAsync(permissionName);
        if (!isGranted)
        {
            throw new HubException($"Permission '{permissionName}' is required");
        }
    }

    /// <summary>
    /// 要求当前用户在指定角色中，否则抛出异常
    /// </summary>
    /// <param name="roles">角色名称列表</param>
    protected void RequireRole(params string[] roles)
    {
        RequireAuthentication();

        if (roles == null || roles.Length == 0)
            return;

        var hasRole = roles.Any(role => IsInRole(role));
        if (!hasRole)
        {
            throw new HubException($"One of the following roles is required: {string.Join(", ", roles)}");
        }
    }

    /// <summary>
    /// 连接建立时调用
    /// </summary>
    /// <returns>任务</returns>
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();

        // 将用户添加到用户组并记录连接
        if (CurrentUserId.HasValue)
        {
            var groupName = $"User_{CurrentUserId.Value}";
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

            // 连接管理记录为辅助操作，不应影响核心连接生命周期
            if (_connectionManager != null)
            {
                try
                {
                    // Build connection metadata from HttpContext
                    var metadata = BuildConnectionMetadata();
                    await _connectionManager.AddConnectionAsync(CurrentUserId.Value, Context.ConnectionId, metadata);

                    // Publish connection event (auxiliary, errors ignored)
                    await PublishConnectionEventAsync();
                }
                catch (Exception ex)
                {
                    // 连接管理失败不应阻止用户连接
                    GetLogger()?.LogWarning(ex,
                        "Failed to track connection for user {UserId}. ConnectionId: {ConnectionId}",
                        CurrentUserId.Value, Context.ConnectionId);
                }
            }
        }
    }

    /// <summary>
    /// 连接断开时调用
    /// </summary>
    /// <param name="exception">异常</param>
    /// <returns>任务</returns>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // 从用户组移除并清理连接记录
        if (CurrentUserId.HasValue)
        {
            var groupName = $"User_{CurrentUserId.Value}";
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

            // 连接管理清理为辅助操作，不应影响核心断开生命周期
            if (_connectionManager != null)
            {
                try
                {
                    await _connectionManager.RemoveConnectionAsync(CurrentUserId.Value, Context.ConnectionId);

                    // Publish disconnection event (auxiliary, errors ignored)
                    await PublishDisconnectionEventAsync(exception);
                }
                catch (Exception ex)
                {
                    // 连接管理失败不应阻止用户断开
                    GetLogger()?.LogWarning(ex,
                        "Failed to remove tracked connection for user {UserId}. ConnectionId: {ConnectionId}",
                        CurrentUserId.Value, Context.ConnectionId);
                }
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Build connection metadata from the current HttpContext
    /// </summary>
    private ConnectionMetadata BuildConnectionMetadata()
    {
        var httpContext = Context.GetHttpContext();
        return new ConnectionMetadata
        {
            ConnectionId = Context.ConnectionId,
            UserId = CurrentUserId,
            UserName = CurrentUserName,
            ConnectedAt = DateTime.UtcNow,
            // 受 AspNetCoreOptions.CollectClientIpAddress 约束（连接元数据会被持久化与展示）。
            IpAddress = httpContext?.Request?.GetClientIp(),
            UserAgent = httpContext?.Request?.Headers["User-Agent"].ToString(),
            HubName = GetType().Name,
        };
    }

    /// <summary>
    /// Publish user connected event via EventBus (auxiliary operation)
    /// </summary>
    private async Task PublishConnectionEventAsync()
    {
        var eventBus = GetEventBus();
        if (eventBus == null || !CurrentUserId.HasValue) return;

        try
        {
            var connectionCount = await _connectionManager!.GetConnectionCountAsync(CurrentUserId.Value);
            await eventBus.PublishAsync(new Events.UserConnectedEvent
            {
                UserId = CurrentUserId.Value,
                ConnectionId = Context.ConnectionId,
                HubName = GetType().Name,
                UserName = CurrentUserName,
                IpAddress = Context.GetHttpContext()?.Request?.GetClientIp(),
                TotalConnections = connectionCount,
            });
        }
        catch (Exception ex)
        {
            GetLogger()?.LogDebug(ex, "Failed to publish UserConnectedEvent");
        }
    }

    /// <summary>
    /// Publish user disconnected event via EventBus (auxiliary operation)
    /// </summary>
    private async Task PublishDisconnectionEventAsync(Exception? disconnectException)
    {
        var eventBus = GetEventBus();
        if (eventBus == null || !CurrentUserId.HasValue) return;

        try
        {
            var remainingCount = await _connectionManager!.GetConnectionCountAsync(CurrentUserId.Value);
            await eventBus.PublishAsync(new Events.UserDisconnectedEvent
            {
                UserId = CurrentUserId.Value,
                ConnectionId = Context.ConnectionId,
                HubName = GetType().Name,
                Reason = disconnectException?.Message,
                RemainingConnections = remainingCount,
                WentOffline = remainingCount == 0,
            });
        }
        catch (Exception ex)
        {
            GetLogger()?.LogDebug(ex, "Failed to publish UserDisconnectedEvent");
        }
    }

    /// <summary>
    /// 获取日志记录器（从 Hub 上下文的请求服务中延迟解析）
    /// </summary>
    private ILogger? GetLogger()
    {
        try
        {
            var loggerFactory = Context.GetHttpContext()?.RequestServices?.GetService<ILoggerFactory>();
            return loggerFactory?.CreateLogger(GetType());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取事件总线（从 Hub 上下文的请求服务中延迟解析）
    /// </summary>
    private IEventBus? GetEventBus()
    {
        try
        {
            return Context.GetHttpContext()?.RequestServices?.GetService<IEventBus>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 将用户添加到指定组 (SignalR group + ConnectionManager tracking)
    /// </summary>
    /// <param name="groupId">组ID</param>
    /// <returns>任务</returns>
    protected async Task AddToGroupAsync(string groupId)
    {
        Check.NotNullOrWhiteSpace(groupId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupId);

        // Track in connection manager as well
        if (_connectionManager != null)
        {
            try
            {
                await _connectionManager.AddToGroupAsync(Context.ConnectionId, groupId);
            }
            catch (Exception ex)
            {
                GetLogger()?.LogDebug(ex, "Failed to track group membership for {ConnectionId} in group {GroupId}",
                    Context.ConnectionId, groupId);
            }
        }
    }

    /// <summary>
    /// 将用户从指定组移除 (SignalR group + ConnectionManager tracking)
    /// </summary>
    /// <param name="groupId">组ID</param>
    /// <returns>任务</returns>
    protected async Task RemoveFromGroupAsync(string groupId)
    {
        Check.NotNullOrWhiteSpace(groupId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);

        // Track in connection manager as well
        if (_connectionManager != null)
        {
            try
            {
                await _connectionManager.RemoveFromGroupAsync(Context.ConnectionId, groupId);
            }
            catch (Exception ex)
            {
                GetLogger()?.LogDebug(ex, "Failed to remove group membership for {ConnectionId} from group {GroupId}",
                    Context.ConnectionId, groupId);
            }
        }
    }

    /// <summary>
    /// Get the typed client proxy for a specific user (by user ID).
    /// Uses the convention-based user group "User_{userId}" to target all of a user's connections.
    /// </summary>
    /// <param name="userId">Target user ID</param>
    /// <returns>Typed client proxy for the user</returns>
    protected TClient UserClient(Guid userId)
    {
        var groupName = $"User_{userId}";
        return Clients.Group(groupName);
    }

    /// <summary>
    /// Get the typed client proxy for all clients except the caller
    /// </summary>
    /// <returns>Typed client proxy excluding the caller</returns>
    protected TClient OthersClient()
    {
        return Clients.AllExcept(Context.ConnectionId);
    }

    /// <summary>
    /// Get the typed client proxy for all clients in a group except the caller
    /// </summary>
    /// <param name="groupName">Group name</param>
    /// <returns>Typed client proxy for the group excluding the caller</returns>
    protected TClient GroupExceptCallerClient(string groupName)
    {
        Check.NotNullOrWhiteSpace(groupName);
        return Clients.GroupExcept(groupName, Context.ConnectionId);
    }
}

/// <summary>
/// Tnzi SignalR Hub 基类 (非强类型版本，向后兼容)
/// </summary>
public abstract class TnziHub : TnziHub<ITnziHubClient>
{
    /// <summary>
    /// 初始化一个<see cref="TnziHub"/>类型的新实例
    /// </summary>
    protected TnziHub()
    {
    }

    /// <summary>
    /// 初始化一个<see cref="TnziHub"/>类型的新实例
    /// </summary>
    /// <param name="connectionManager">连接管理器</param>
    /// <param name="permissionChecker">权限检查器 (可选)</param>
    protected TnziHub(IConnectionManager connectionManager, IPermissionChecker? permissionChecker = null)
        : base(connectionManager, permissionChecker)
    {
    }
}
