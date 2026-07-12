namespace Tnzi.Security.Authorization;

/// <summary>
/// 权限定义上下文接口
/// </summary>
public interface IPermissionDefinitionContext
{
    /// <summary>
    /// 添加权限组
    /// </summary>
    /// <param name="defaultCategory">组内权限的默认分类；null = Business。</param>
    PermissionGroupDefinition AddGroup(string name, string displayName, string? description = null, string? parentName = null, PermissionCategory? defaultCategory = null);

    /// <summary>
    /// 添加权限
    /// </summary>
    /// <param name="category">权限分类；null = 继承所属组的 DefaultCategory（组不存在则 Business）。</param>
    PermissionDefinition AddPermission(string name, string displayName, string? description = null, string? parentName = null, PermissionCategory? category = null);
}

