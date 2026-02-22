
namespace Tnzi.Security.Authorization;

/// <summary>
/// API 功能授权特性
/// 继承自 AuthorizeAttribute，通过授权策略系统进行权限验证。
/// 支持两种使用方式：
/// 1. 仅需登录：[ApiAuthorize] - 任意已登录用户可访问
/// 2. 需登录且指定权限：[ApiAuthorize(PermissionName = "Admin.User.Manage")] - 需登录并拥有该权限
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class ApiAuthorizeAttribute : AuthorizeAttribute
{
    private const string PermissionPolicyPrefix = "Permission:";
    private const string DefaultPolicy = "FunctionAuthorization";

    /// <summary>
    /// 获取或设置 权限名称
    /// 未指定时 [ApiAuthorize] 仅验证登录；指定后需同时拥有该权限
    /// </summary>
    public string? PermissionName
    {
        get
        {
            if (!string.IsNullOrEmpty(Policy) && Policy.StartsWith(PermissionPolicyPrefix))
            {
                return Policy[PermissionPolicyPrefix.Length..];
            }
            return null;
        }
        set
        {
            if (!string.IsNullOrEmpty(value))
            {
                Policy = PermissionPolicyPrefix + value;
            }
            else
            {
                Policy = DefaultPolicy;
            }
        }
    }

    /// <summary>
    /// 初始化一个<see cref="ApiAuthorizeAttribute"/>类型的新实例
    /// 仅验证用户已登录，不校验具体权限
    /// </summary>
    public ApiAuthorizeAttribute()
    {
        Policy = DefaultPolicy;
    }

    /// <summary>
    /// 初始化一个<see cref="ApiAuthorizeAttribute"/>类型的新实例
    /// </summary>
    /// <param name="permissionName">权限名称</param>
    public ApiAuthorizeAttribute(string permissionName)
    {
        PermissionName = permissionName;
    }
}