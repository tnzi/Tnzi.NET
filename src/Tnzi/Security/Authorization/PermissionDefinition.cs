namespace Tnzi.Security.Authorization;

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

    // 权限模型是扁平的 deny-by-default 码集合：一次判定只问「用户的有效集里有没有这个码」。
    // 早期这里还有 RequiresPermission(...)（依赖码）与 InheritFromParent()（持父码即持子码）两个链式 API，
    // 但唯一兑现它们的 PermissionManager.IsGrantedAsync 从未接入任何运行时判定路径，
    // 消费方按 XML 文档写下的约束一行都不生效。2026-09-12 起整组删除，别再加回一个没人执行的承诺。
}
