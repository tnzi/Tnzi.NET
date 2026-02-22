namespace Tnzi.Authorization.Permissions;

/// <summary>
/// 权限定义上下文接口
/// </summary>
public interface IPermissionDefinitionContext
{
    /// <summary>
    /// 添加权限组
    /// </summary>
    PermissionGroupDefinition AddGroup(string name, string displayName, string? description = null, string? parentName = null);

    /// <summary>
    /// 添加权限
    /// </summary>
    PermissionDefinition AddPermission(string name, string displayName, string? description = null, string? parentName = null);
}

