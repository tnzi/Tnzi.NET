

namespace Tnzi.SignalR.Filters;

/// <summary>
/// Hub 授权过滤器
/// 复用框架的 IPermissionChecker 进行权限验证
///
/// ★ 两个把关点缺一不可。<see cref="HubAuthorizeAttribute"/> 允许标在**类**上，
/// 而类级授权的意思是"这个 Hub 只给授权用户用" —— Hub 最主要的能力是**接收广播**，
/// 那不需要调用任何方法。只在 <see cref="InvokeMethodAsync"/> 把关的话，未认证客户端
/// 照样能完成握手、进入组、收到服务端推送的每一条消息，只有主动调方法时才被拒。
/// </summary>
public class HubAuthorizationFilter : IHubFilter
{
    private readonly ILogger<HubAuthorizationFilter> _logger;
    private readonly IPermissionChecker? _permissionChecker;

    /// <summary>
    /// 初始化一个<see cref="HubAuthorizationFilter"/>类型的新实例
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="permissionChecker">权限检查器（可选；未加载 Authorization 模块时为 null）</param>
    public HubAuthorizationFilter(ILogger<HubAuthorizationFilter> logger, IPermissionChecker? permissionChecker = null)
    {
        _logger = Check.NotNull(logger);
        _permissionChecker = permissionChecker;
    }

    /// <summary>
    /// 建立连接时的授权检查（只看**类级**特性 —— 连接阶段还没有方法可言）。
    /// </summary>
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        Check.NotNull(context);
        Check.NotNull(next);

        var hubType = context.Hub.GetType();
        // inherit: true —— HubAuthorizeAttribute 声明为 Inherited，按文档写的
        // `class MyHub : SomeAuthorizedHub` 必须继承到基类的要求。
        var hubAuth = hubType.GetCustomAttribute<HubAuthorizeAttribute>(inherit: true);
        if (hubAuth != null)
        {
            await AuthorizeAsync(hubAuth, context.Context.User, hubType.Name, methodName: null);
        }

        await next(context);
    }

    /// <summary>
    /// 调用 Hub 方法时的授权检查（方法级特性优先，其次类级）
    /// </summary>
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        Check.NotNull(invocationContext);
        Check.NotNull(next);

        var method = invocationContext.HubMethod;

        // 获取 HubAuthorize 特性 (方法级别优先，其次类级别)
        var hubAuth = method.GetCustomAttribute<HubAuthorizeAttribute>()
            ?? invocationContext.Hub.GetType().GetCustomAttribute<HubAuthorizeAttribute>(inherit: true);

        // 如果没有授权特性，直接放行
        if (hubAuth != null)
        {
            await AuthorizeAsync(
                hubAuth,
                invocationContext.Context.User,
                invocationContext.Hub.GetType().Name,
                invocationContext.HubMethodName);
        }

        return await next(invocationContext);
    }

    /// <summary>
    /// 认证 → 角色 → 权限，三道顺序检查。连接期与方法调用期共用同一份，
    /// 两处分别手写会让其中一处慢慢落后于另一处。
    /// </summary>
    /// <param name="hubAuth">生效的授权特性</param>
    /// <param name="user">当前主体</param>
    /// <param name="hubName">Hub 类型名（仅用于日志与错误信息）</param>
    /// <param name="methodName">方法名；连接期为 null</param>
    private async Task AuthorizeAsync(
        HubAuthorizeAttribute hubAuth,
        ClaimsPrincipal? user,
        string hubName,
        string? methodName)
    {
        var target = methodName == null ? hubName : $"{hubName}.{methodName}";

        // 检查认证状态
        if (user?.Identity?.IsAuthenticated != true)
        {
            _logger.LogWarning("Unauthenticated access to hub {Target}", target);
            throw new HubException("User is not authenticated");
        }

        // 检查角色
        if (!string.IsNullOrEmpty(hubAuth.Roles))
        {
            var roles = hubAuth.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var hasRole = roles.Any(r => user.IsInRoleIgnoreCase(r.Trim()));
            if (!hasRole)
            {
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.Identity?.Name;
                _logger.LogWarning(
                    "User {UserId} lacks required role for hub {Target}. Required: {Roles}",
                    userId, target, hubAuth.Roles);
                throw new HubException("Access denied. Required role not found.");
            }
        }

        // 检查权限 (复用框架权限检查器)
        if (!string.IsNullOrEmpty(hubAuth.PermissionName))
        {
            if (_permissionChecker == null || _permissionChecker is NullPermissionChecker)
            {
                _logger.LogWarning("Permission checker not available for hub {Target}", target);
                throw new HubException("Authorization service unavailable.");
            }

            var hasPermission = await _permissionChecker.IsGrantedAsync(hubAuth.PermissionName);
            if (!hasPermission)
            {
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.Identity?.Name;
                _logger.LogWarning(
                    "User {UserId} lacks permission {Permission} for hub {Target}",
                    userId, hubAuth.PermissionName, target);
                throw new HubException($"Access denied. Permission '{hubAuth.PermissionName}' required.");
            }
        }
    }
}
