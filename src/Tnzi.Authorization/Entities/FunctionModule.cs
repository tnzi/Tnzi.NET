namespace Tnzi.Authorization.Entities;

/// <summary>
/// 功能模块实体
/// </summary>
public class FunctionModule : FullAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 模块名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 模块代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 模块描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 获取或设置 排序号
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 获取或设置 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 获取或设置 父模块ID
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// 获取或设置 父模块
    /// </summary>
    public virtual FunctionModule? Parent { get; set; }

    /// <summary>
    /// 获取或设置 子模块集合
    /// </summary>
    public virtual ICollection<FunctionModule> Children { get; set; } = new List<FunctionModule>();

    /// <summary>
    /// 获取或设置 功能集合
    /// </summary>
    public virtual ICollection<ModuleFunction> Functions { get; set; } = new List<ModuleFunction>();
}
