namespace Tnzi.Template.Entities;

/// <summary>
/// 通用布局实体（支持所有业务场景）
/// </summary>
public class Layout : FullAuditedEntity<Guid>
{
    /// <summary>
    /// Layout名称（在模块+分类下唯一）
    /// </summary>
    public string LayoutName { get; set; } = string.Empty;

    /// <summary>
    /// 所属模块
    /// </summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>
    /// Layout分类
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Layout内容（Razor模板）
    /// </summary>
    public string LayoutContent { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 是否默认Layout
    /// </summary>
    public bool IsDefault { get; set; } = false;

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 扩展元数据（JSON格式）
    /// </summary>
    public string? Metadata { get; set; }
}
