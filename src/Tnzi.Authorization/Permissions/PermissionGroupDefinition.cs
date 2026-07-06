namespace Tnzi.Authorization.Permissions;

/// <summary>
/// 权限组定义（对应Module）
/// </summary>
public class PermissionGroupDefinition
{
    /// <summary>
    /// 组名称（对应Module.Code）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称（对应Module.Name）
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 描述（对应Module.Description）
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 父组名称（基于Module.ParentId）
    /// </summary>
    public string? ParentName { get; set; }

    /// <summary>
    /// 是否启用（对应Module.IsEnabled）
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 组内权限的默认分类：AddPermission 未显式指定 category 时继承此值。
    /// </summary>
    public PermissionCategory DefaultCategory { get; set; } = PermissionCategory.Business;

    /// <summary>
    /// 权限列表（对应Module.Functions）
    /// </summary>
    public List<PermissionDefinition> Permissions { get; set; } = new();
}

