namespace Tnzi.Authorization.Entities;

/// <summary>
/// 模块功能实体
/// </summary>
public class ModuleFunction : FullAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 功能名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 功能代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 功能描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 获取或设置 所属模块ID
    /// </summary>
    public Guid ModuleId { get; set; }

    /// <summary>
    /// 获取或设置 所属模块
    /// </summary>
    public virtual FunctionModule FunctionModule { get; set; } = null!;

    /// <summary>
    /// 获取或设置 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 获取或设置 排序号
    /// </summary>
    public int Order { get; set; }
}

