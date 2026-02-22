namespace Tnzi.SignalR.Authorization;

/// <summary>
/// Hub 方法授权特性
/// 设计与 ApiAuthorizeAttribute 保持一致
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class HubAuthorizeAttribute : Attribute
{
    /// <summary>
    /// 获取或设置 权限名称
    /// </summary>
    public string? PermissionName { get; set; }

    /// <summary>
    /// 获取或设置 角色要求 (逗号分隔)
    /// </summary>
    public string? Roles { get; set; }

    /// <summary>
    /// 获取或设置 是否仅验证认证状态
    /// 当 PermissionName 和 Roles 都为空时，仅验证用户已认证
    /// </summary>
    public bool AuthenticatedOnly => string.IsNullOrEmpty(PermissionName) && string.IsNullOrEmpty(Roles);

    /// <summary>
    /// 初始化一个仅验证认证的授权特性
    /// </summary>
    public HubAuthorizeAttribute()
    {
    }

    /// <summary>
    /// 初始化一个验证权限的授权特性
    /// </summary>
    /// <param name="permissionName">权限名称</param>
    public HubAuthorizeAttribute(string permissionName)
    {
        PermissionName = Check.NotNullOrWhiteSpace(permissionName);
    }
}
