using Tnzi.Security.Claims;

namespace Tnzi.Hangfire;

/// <summary>
/// Dashboard 基于角色的授权过滤器
/// </summary>
internal class DashboardRoleAuthorizationFilter : IDashboardAuthorizationFilter
{
    private readonly string[] _allowedRoles;

    /// <summary>
    /// 初始化授权过滤器
    /// </summary>
    /// <param name="allowedRoles">允许访问的角色列表</param>
    public DashboardRoleAuthorizationFilter(string[] allowedRoles)
    {
        Check.NotNullOrEmpty(allowedRoles);
        _allowedRoles = allowedRoles;
    }

    /// <summary>
    /// 验证用户是否有权限访问 Dashboard
    /// </summary>
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // 如果用户未认证，拒绝访问
        if (!(httpContext.User.Identity?.IsAuthenticated ?? false))
        {
            return false;
        }

        // 检查用户是否具有允许的角色
        // 构造函数已通过 Check.NotNullOrEmpty 保证 _allowedRoles 非空
        return _allowedRoles.Any(role => httpContext.User.IsInRoleIgnoreCase(role));
    }
}
