
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
                    await _connectionManager.AddConnectionAsync(CurrentUserId.Value, Context.ConnectionId);
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
    /// 将用户添加到指定组
    /// </summary>
    /// <param name="groupId">组ID</param>
    /// <returns>任务</returns>
    protected async Task AddToGroupAsync(string groupId)
    {
        Check.NotNullOrWhiteSpace(groupId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupId);
    }

    /// <summary>
    /// 将用户从指定组移除
    /// </summary>
    /// <param name="groupId">组ID</param>
    /// <returns>任务</returns>
    protected async Task RemoveFromGroupAsync(string groupId)
    {
        Check.NotNullOrWhiteSpace(groupId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);
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