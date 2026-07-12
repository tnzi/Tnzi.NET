
namespace Tnzi.Security.Authorization;

/// <summary>
/// API 功能授权特性
/// 继承自 AuthorizeAttribute，通过授权策略系统进行权限验证。
/// 支持两种使用方式：
/// 1. 仅需登录：[ApiAuthorize] - 任意已登录用户可访问
/// 2. 需登录且指定权限：[ApiAuthorize(PermissionName = "Admin.User.Manage")] - 需登录并拥有该权限
/// </summary>
/// <remarks>
/// <b>叠加语义（AllowMultiple = true）</b>：同一端点上收集到的多个
/// <see cref="ApiAuthorizeAttribute"/>（基类 + 派生类 + 方法级）是 <b>AND</b>
/// 关系——每个声明的权限都必须通过。框架据此实现 admin 门禁：
/// <c>ApiAdminControllerBase</c> 提供认证边界（裸门），各模块
/// <c>Default*AdminController</c> 类级声明本模块 <c>.view</c> 码（如
/// <c>finance.account.view</c>），写端点再叠加方法级操作码（如
/// <c>finance.account.create</c>），派生类/方法级特性不会替换基类特性。
/// <c>[AllowAnonymous]</c> 仍然豁免全部检查。
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
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