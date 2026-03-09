namespace Tnzi.Authorization.Entities;

/// <summary>
/// 模块用户实体（用户与模块的关联）
/// </summary>
public class ModuleUser : MultiTenantAuditedEntity<Guid>
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
    /// 获取或设置 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 获取或设置 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

