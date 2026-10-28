namespace Tnzi.Security.Authorization;

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
    /// 组归属：平台（运营方的管理面）还是应用（使用方自己的业务）。<c>null</c> = 未声明，
    /// 由声明它的 provider 所在程序集推出（<c>Tnzi.*</c> 为平台，其余为应用）。
    /// 只在 <c>Authorization:SuperAdminBypassScope = Platform</c> 时影响访问：超管只对平台组的码走旁路。
    /// </summary>
    /// <remarks>
    /// 共享组（多个 provider 都 <c>AddGroup</c> 同一个名字）只需由其中一个声明；
    /// <c>AddGroup</c> 返回已存在的定义，所以谁设置都落在同一个对象上，与加载顺序无关。
    /// </remarks>
    public PermissionScope? Scope { get; set; }
}

