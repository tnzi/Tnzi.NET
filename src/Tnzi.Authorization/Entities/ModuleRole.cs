namespace Tnzi.Authorization.Entities;

/// <summary>
/// 模块角色实体（角色与模块的关联）
/// </summary>
public class ModuleRole : FullAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 模块ID
    /// </summary>
    public Guid ModuleId { get; set; }

    /// <summary>
    /// 获取或设置 模块
    /// </summary>
    public virtual FunctionModule FunctionModule { get; set; } = null!;

    /// <summary>
    /// 获取或设置 角色ID
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// 获取或设置 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

