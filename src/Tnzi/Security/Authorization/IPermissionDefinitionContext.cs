namespace Tnzi.Security.Authorization;

/// <summary>
/// 权限定义上下文接口
/// </summary>
public interface IPermissionDefinitionContext
{
    /// <summary>
    /// 添加权限组
    /// </summary>
    /// <param name="name">组名（唯一标识，如 "identity"）</param>
    /// <param name="displayName">显示名称</param>
    /// <param name="description">描述</param>
    /// <param name="parentName">父组名；null = 顶级组</param>
    /// <param name="defaultCategory">组内权限的默认分类；null = Business。</param>
    /// <returns>新建或已存在的组定义</returns>
    PermissionGroupDefinition AddGroup(string name, string displayName, string? description = null, string? parentName = null, PermissionCategory? defaultCategory = null);

    /// <summary>
    /// 添加权限
    /// </summary>
    /// <param name="name">权限码（唯一标识，如 "identity.user.create"）</param>
    /// <param name="displayName">显示名称</param>
    /// <param name="description">描述</param>
    /// <param name="parentName">父权限码；null = 无父级</param>
    /// <param name="category">权限分类；null = 继承所属组的 DefaultCategory（组不存在则 Business）。</param>
    /// <returns>新建或已存在的权限定义</returns>
    PermissionDefinition AddPermission(string name, string displayName, string? description = null, string? parentName = null, PermissionCategory? category = null);
}

