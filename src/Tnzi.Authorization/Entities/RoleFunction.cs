namespace Tnzi.Authorization.Entities;

/// <summary>
/// 角色功能实体（角色与功能的关联）
/// </summary>
public class RoleFunction : FullAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 角色ID
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// 获取或设置 功能ID
    /// </summary>
    public Guid FunctionId { get; set; }

    /// <summary>
    /// 获取或设置 功能
    /// </summary>
    public virtual ModuleFunction Function { get; set; } = null!;

    /// <summary>
    /// 获取或设置 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

