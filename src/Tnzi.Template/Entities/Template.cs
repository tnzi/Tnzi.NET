namespace Tnzi.Template.Entities;

/// <summary>
/// 通用模板实体（支持所有业务场景）
/// </summary>
public class Template : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 模板名称（在模块+分类下唯一）
    /// </summary>
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>
    /// 所属模块（如 Notification、Invoice、Contract）
    /// </summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>
    /// 模板分类（如 Email、Invoice、Contract）
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 主题模板（支持Razor语法）
    /// </summary>
    public string SubjectTemplate { get; set; } = string.Empty;

    /// <summary>
    /// 内容模板（支持Razor语法）
    /// </summary>
    public string ContentTemplate { get; set; } = string.Empty;

    /// <summary>
    /// 默认布局名称
    /// </summary>
    public string? DefaultLayoutName { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 扩展元数据（JSON格式）
    /// </summary>
    public string? Metadata { get; set; }
}
