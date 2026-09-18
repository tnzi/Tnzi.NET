// 文件级 using：Tnzi.AspNetCore.Options 与本模块的 Tnzi.SignalR.Options 都有 RateLimitOptions，
// 进 GlobalUsings 会让别的文件产生歧义；这里只要租户 claim 名那一个常量。
using Tnzi.AspNetCore.Options;


namespace Tnzi.SignalR.Hubs;

/// <summary>
/// Tnzi SignalR Hub 基类 (强类型版本)
/// </summary>
/// <typeparam name="TClient">客户端接口类型</typeparam>
public abstract class TnziHub<TClient> : Hub<TClient> where TClient : class
{
    /// <summary>
    /// 连接级缓存键：第一次解析出的 <see cref="IConnectionManager"/> 存进
    /// <c>Context.Items</c>，供同一条连接后续的 Hub 实例复用。
    /// </summary>
    private const string ConnectionManagerItemKey = "Tnzi.SignalR.ConnectionManager";

    /// <summary>
    /// 连接级标记：这条连接已经为「事件总线解析不到」记过一次 Warning。
    /// 连接与断开两端共用，避免同一条连接刷两行一样的话。
    /// </summary>
    private const string EventBusWarnedItemKey = "Tnzi.SignalR.EventBusWarned";

    private readonly IConnectionManager? _injectedConnectionManager;
    private readonly IPermissionChecker? _permissionChecker;

    private IConnectionManager? _connectionManagerCache;
    private bool _connectionManagerResolved;

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
        _injectedConnectionManager = Check.NotNull(connectionManager);
        _permissionChecker = permissionChecker;
    }

    /// <summary>
    /// 生效的连接管理器：构造注入优先，否则从连接的请求服务里解析。
    ///
    /// ★ 为什么必须能自己解析：基类提供了无参构造，用它继承是完全合法的写法
    /// （框架自己的 <c>SettingsRealtimeHub</c> 就是 <c>: TnziHub { }</c>）。
    /// 若此时连接管理器就是 null，这些连接会**静默地**不参与任何追踪 ——
    /// 不出现在 admin 的在线用户/连接查询里、不计入 <c>MaxConnectionsPerUser</c>、
    /// 强制断开对它们无效 —— 而连接本身一切正常，没有任何报错或日志。
    ///
    /// 解析结果同时写进 <c>Context.Items</c>：Hub 实例每次调用都是新的，缓存一份省得
    /// 每次调用重新解析。
    /// </summary>
    private IConnectionManager? ConnectionManager
    {
        get
        {
            if (_injectedConnectionManager != null) return _injectedConnectionManager;
            if (_connectionManagerResolved) return _connectionManagerCache;

            _connectionManagerResolved = true;

            var items = Context?.Items;
            if (items != null
                && items.TryGetValue(ConnectionManagerItemKey, out var cached)
                && cached is IConnectionManager fromItems)
            {
                return _connectionManagerCache = fromItems;
            }

            var resolved = GetRequestService<IConnectionManager>();
            if (resolved != null && items != null)
            {
                items[ConnectionManagerItemKey] = resolved;
            }

            return _connectionManagerCache = resolved;
        }
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
    /// 当前连接主体的租户（<c>tenant_id</c> claim，与 <c>HttpContextCurrentUser.TenantId</c> 同源）；没有 claim 为 null。
    /// </summary>
    protected Guid? CurrentTenantId
    {
        get
        {
            var claim = Context.User?.FindFirst(TenantResolverOptions.DefaultClaimType);
            return claim != null && Guid.TryParse(claim.Value, out var tenantId) ? tenantId : null;
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

        // 登记到中断表。刻意**不**限于已登录用户：这张表按 connectionId 索引，
        // 匿名连接同样应该是可中断的。
        GetRequestService<IHubConnectionAborter>()?.Register(Context.ConnectionId, Context);

        // 将用户添加到用户组并记录连接
        if (CurrentUserId.HasValue)
        {
            var groupName = HubGroupNames.ForUser(CurrentUserId.Value);
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

            // ★ 带租户 claim 的连接同时进租户组：「发给整个租户」的推送投递到它，而不是 Clients.All。
            //   没有 claim 的连接不进任何租户组（关闭方向：按租户推送时收不到，而不是收到所有租户的）。
            if (CurrentTenantId is { } tenantId)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, HubGroupNames.ForTenant(tenantId));
            }

            // 连接管理记录为辅助操作，不应影响核心连接生命周期
            var connectionManager = ConnectionManager;
            if (connectionManager != null)
            {
                try
                {
                    // Build connection metadata from HttpContext
                    var metadata = BuildConnectionMetadata();
                    await connectionManager.AddConnectionAsync(CurrentUserId.Value, Context.ConnectionId, metadata);

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
        GetRequestService<IHubConnectionAborter>()?.Unregister(Context.ConnectionId);

        // 从用户组移除并清理连接记录
        if (CurrentUserId.HasValue)
        {
            var groupName = HubGroupNames.ForUser(CurrentUserId.Value);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

            if (CurrentTenantId is { } tenantId)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroupNames.ForTenant(tenantId));
            }

            // 连接管理清理为辅助操作，不应影响核心断开生命周期
            var connectionManager = ConnectionManager;
            if (connectionManager != null)
            {
                try
                {
                    await connectionManager.RemoveConnectionAsync(CurrentUserId.Value, Context.ConnectionId);

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
        var connectionManager = ConnectionManager;
        if (eventBus == null || connectionManager == null || !CurrentUserId.HasValue) return;

        try
        {
            var connectionCount = await connectionManager.GetConnectionCountAsync(CurrentUserId.Value);
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
        var connectionManager = ConnectionManager;
        if (eventBus == null || connectionManager == null || !CurrentUserId.HasValue) return;

        try
        {
            var remainingCount = await connectionManager.GetConnectionCountAsync(CurrentUserId.Value);
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
    /// 延迟解析一个可选服务。
    ///
    /// Hub 的构造发生在 DI 之外（无参构造是允许的），而这些能力都是可选的：解析不到就
    /// 少一项辅助功能，不该让连接失败。
    ///
    /// ★ 来源顺序：先是 <see cref="HubInvocationServicesFilter"/> 放进 <c>Context.Items</c>
    /// 的 Hub 激活作用域（三种传输都有，且在本次回调返回前不会被释放），其次才是
    /// <c>HttpContext.RequestServices</c>。后者在长轮询下靠不住：SignalR 给长轮询连接的是
    /// 一份克隆的 <c>HttpContext</c>，它的作用域随连接释放而释放，而
    /// <see cref="OnDisconnectedAsync"/> 在那之后才跑 —— 只从它解析的话，断开事件与
    /// 中断表注销在长轮询下一律静默丢失。吞掉解析异常是刻意的，调用方按 null 处理。
    /// </summary>
    private TService? GetRequestService<TService>() where TService : class
    {
        var items = Context?.Items;
        if (items != null
            && items.TryGetValue(HubInvocationServicesFilter.ItemKey, out var exposed)
            && exposed is IServiceProvider invocationServices)
        {
            var fromInvocation = TryGetService<TService>(invocationServices);
            if (fromInvocation != null) return fromInvocation;
        }

        return TryGetService<TService>(Context?.GetHttpContext()?.RequestServices);
    }

    private static TService? TryGetService<TService>(IServiceProvider? services) where TService : class
    {
        if (services == null) return null;

        try
        {
            return services.GetService<TService>();
        }
        catch (ObjectDisposedException)
        {
            // 作用域已被释放（长轮询断开时的 HttpContext 克隆体就是这个形状）
            return null;
        }
        catch (InvalidOperationException)
        {
            // 作用域已不可用于解析
            return null;
        }
    }

    /// <summary>
    /// 获取日志记录器（延迟解析）
    /// </summary>
    private ILogger? GetLogger() =>
        GetRequestService<ILoggerFactory>()?.CreateLogger(GetType());

    /// <summary>
    /// 获取事件总线（延迟解析）。
    ///
    /// 解析不到时按连接记一次 Warning：此前「事件总线模块未加载」「作用域已释放」
    /// 「一切正常」三种情况在日志里一模一样，连接事件掉了没有任何症状。
    /// </summary>
    private IEventBus? GetEventBus()
    {
        var eventBus = GetRequestService<IEventBus>();
        if (eventBus != null) return eventBus;

        var items = Context?.Items;
        if (items != null && !items.ContainsKey(EventBusWarnedItemKey))
        {
            items[EventBusWarnedItemKey] = true;
            GetLogger()?.LogWarning(
                "IEventBus could not be resolved for connection {ConnectionId} on {HubName}; connection events will not be published for it",
                Context?.ConnectionId, GetType().Name);
        }

        return null;
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
        var connectionManager = ConnectionManager;
        if (connectionManager != null)
        {
            try
            {
                await connectionManager.AddToGroupAsync(Context.ConnectionId, groupId);
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
        var connectionManager = ConnectionManager;
        if (connectionManager != null)
        {
            try
            {
                await connectionManager.RemoveFromGroupAsync(Context.ConnectionId, groupId);
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
    /// Uses the convention-based user group (<see cref="HubGroupNames.ForUser"/>) to target all of a user's connections.
    /// </summary>
    /// <param name="userId">Target user ID</param>
    /// <returns>Typed client proxy for the user</returns>
    protected TClient UserClient(Guid userId)
    {
        var groupName = HubGroupNames.ForUser(userId);
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
