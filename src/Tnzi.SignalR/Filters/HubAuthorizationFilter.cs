
namespace Tnzi.SignalR.Filters;

/// <summary>
/// Hub 授权过滤器
/// 复用框架的 IPermissionChecker 进行权限验证
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
    /// 调用 Hub 方法时的授权检查
    /// </summary>
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var method = invocationContext.HubMethod;
        var user = invocationContext.Context.User;

        // 获取 HubAuthorize 特性 (方法级别优先，其次类级别)
        var hubAuth = method.GetCustomAttribute<HubAuthorizeAttribute>()
            ?? method.DeclaringType?.GetCustomAttribute<HubAuthorizeAttribute>();

        // 如果没有授权特性，直接放行
        if (hubAuth == null)
        {
            return await next(invocationContext);
        }

        // 检查认证状态
        if (user?.Identity?.IsAuthenticated != true)
        {
            _logger.LogWarning(
                "Unauthenticated access to hub method {HubName}.{MethodName}",
                invocationContext.Hub.GetType().Name,
                invocationContext.HubMethodName);
            throw new HubException("User is not authenticated");
        }

        // 检查角色
        if (!string.IsNullOrEmpty(hubAuth.Roles))
        {
            var roles = hubAuth.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var hasRole = roles.Any(r => user.IsInRole(r.Trim()));
            if (!hasRole)
            {
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.Identity?.Name;
                _logger.LogWarning(
                    "User {UserId} lacks required role for hub method {HubName}.{MethodName}. Required: {Roles}",
                    userId,
                    invocationContext.Hub.GetType().Name,
                    invocationContext.HubMethodName,
                    hubAuth.Roles);
                throw new HubException("Access denied. Required role not found.");
            }
        }

        // 检查权限 (复用框架权限检查器)
        if (!string.IsNullOrEmpty(hubAuth.PermissionName))
        {
            if (_permissionChecker == null || _permissionChecker is NullPermissionChecker)
            {
                _logger.LogWarning(
                    "Permission checker not available for hub method {HubName}.{MethodName}",
                    invocationContext.Hub.GetType().Name,
                    invocationContext.HubMethodName);
                throw new HubException("Authorization service unavailable.");
            }

            var hasPermission = await _permissionChecker.IsGrantedAsync(hubAuth.PermissionName);
            if (!hasPermission)
            {
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.Identity?.Name;
                _logger.LogWarning(
                    "User {UserId} lacks permission {Permission} for hub method {HubName}.{MethodName}",
                    userId,
                    hubAuth.PermissionName,
                    invocationContext.Hub.GetType().Name,
                    invocationContext.HubMethodName);
                throw new HubException($"Access denied. Permission '{hubAuth.PermissionName}' required.");
            }
        }

        return await next(invocationContext);
    }
}