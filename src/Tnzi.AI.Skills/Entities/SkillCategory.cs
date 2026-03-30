namespace Tnzi.AI.Entities;

/// <summary>
/// 技能分类实体 — 支持层级目录结构组织技能。
/// </summary>
public class SkillCategory : AuditedEntity<Guid>, IMultiTenant
{
    /// <summary>租户 ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>分类名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>URL 友好的标识符（小写字母/数字/连字符）</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>分类描述</summary>
    public string? Description { get; set; }

    /// <summary>父分类 ID（null 表示顶级分类）</summary>
    public Guid? ParentId { get; set; }

    /// <summary>排序序号（值越小越靠前）</summary>
    public int SortOrder { get; set; }

    /// <summary>图标标识</summary>
    public string? Icon { get; set; }

    /// <summary>父分类导航属性</summary>
    public SkillCategory? Parent { get; set; }

    /// <summary>子分类集合</summary>
    public ICollection<SkillCategory> Children { get; set; } = [];
}
