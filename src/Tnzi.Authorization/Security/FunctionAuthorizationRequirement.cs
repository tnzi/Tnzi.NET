namespace Tnzi.Authorization.Security;

/// <summary>
/// 功能授权要求
/// </summary>
public class FunctionAuthorizationRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// 获取或设置 权限名称
    /// </summary>
    public string? PermissionName { get; set; }

    /// <summary>
    /// 初始化一个<see cref="FunctionAuthorizationRequirement"/>类型的新实例
    /// </summary>
    public FunctionAuthorizationRequirement()
    {
    }

    /// <summary>
    /// 初始化一个<see cref="FunctionAuthorizationRequirement"/>类型的新实例
    /// </summary>
    /// <param name="permissionName">权限名称</param>
    public FunctionAuthorizationRequirement(string permissionName)
    {
        PermissionName = permissionName;
    }
}
