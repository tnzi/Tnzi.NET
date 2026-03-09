namespace Tnzi.Identity.Entities;

/// <summary>
/// 组织架构实体
/// </summary>
public class Organization : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 组织名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 组织代码（唯一标识）
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// 获取或设置 描述/备注
    /// </summary>
    public string? Remark { get; set; }

    /// <summary>
    /// 获取或设置 父组织ID
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// 获取或设置 父组织
    /// </summary>
    public virtual Organization? Parent { get; set; }

    /// <summary>
    /// 获取或设置 子组织列表
    /// </summary>
    public virtual ICollection<Organization> Children { get; set; } = new List<Organization>();

    /// <summary>
    /// 获取或设置 排序号
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 获取或设置 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 获取或设置 组织层级路径（如：/1/2/3/）
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// 获取或设置 组织层级深度
    /// </summary>
    public int Level { get; set; }
}

