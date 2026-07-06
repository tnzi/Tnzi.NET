namespace Tnzi.Authorization.Permissions;

/// <summary>
/// 权限定义
/// </summary>
public class PermissionDefinition
{
    /// <summary>
    /// 权限名称（唯一标识，对应ModuleFunction.Code）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称（对应ModuleFunction.Name）
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 描述（对应ModuleFunction.Description）
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 父权限名称（用于权限分组，基于Module的层次结构）
    /// </summary>
    public string? ParentName { get; set; }

    /// <summary>
    /// 是否启用（对应ModuleFunction.IsEnabled）
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 权限分类（对应ModuleFunction.Category）。
    /// Business = 业务管理员可达；Technical = 仅显式授权或超管可达。
    /// </summary>
    public PermissionCategory Category { get; set; } = PermissionCategory.Business;

    /// <summary>
    /// 权限组（对应Module）
    /// </summary>
    public PermissionGroupDefinition? Group { get; set; }
    
    /// <summary>
    /// 是否从父权限继承
    /// 如果为 true，用户拥有父权限时自动拥有此权限
    /// </summary>
    public bool IsInheritedFromParent { get; set; } = false;
    
    /// <summary>
    /// 依赖的权限名称列表
    /// 检查此权限时，必须同时拥有所有依赖权限
    /// </summary>
    public List<string> RequiredPermissions { get; set; } = new();
    
    /// <summary>
    /// 添加依赖权限
    /// </summary>
    /// <param name="permissionName">权限名称</param>
    /// <returns>当前权限定义（支持链式调用）</returns>
    public PermissionDefinition RequiresPermission(string permissionName)
    {
        if (!string.IsNullOrEmpty(permissionName) && !RequiredPermissions.Contains(permissionName))
        {
            RequiredPermissions.Add(permissionName);
        }
        return this;
    }
    
    /// <summary>
    /// 设置从父权限继承
    /// </summary>
    /// <param name="inherit">是否继承</param>
    /// <returns>当前权限定义（支持链式调用）</returns>
    public PermissionDefinition InheritFromParent(bool inherit = true)
    {
        IsInheritedFromParent = inherit;
        return this;
    }
}

