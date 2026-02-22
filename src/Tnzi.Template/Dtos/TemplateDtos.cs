namespace Tnzi.Template.Dtos;

/// <summary>
/// 模板详情输出 DTO
/// </summary>
public class TemplateDto
{
    /// <summary>
    /// 模板ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 模板名称
    /// </summary>
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>
    /// 所属模块
    /// </summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>
    /// 模板分类
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 主题模板
    /// </summary>
    public string SubjectTemplate { get; set; } = string.Empty;

    /// <summary>
    /// 内容模板
    /// </summary>
    public string ContentTemplate { get; set; } = string.Empty;

    /// <summary>
    /// 默认布局名称
    /// </summary>
    public string? DefaultLayoutName { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 扩展元数据
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 最后修改时间
    /// </summary>
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// 模板请求基类（Create/Update共用字段）
/// </summary>
public abstract class TemplateRequestBase
{
    /// <summary>
    /// 模板名称
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string TemplateName { get; set; } = null!;

    /// <summary>
    /// 所属模块
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Module { get; set; } = null!;

    /// <summary>
    /// 模板分类
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Category { get; set; } = null!;

    /// <summary>
    /// 主题模板
    /// </summary>
    public string SubjectTemplate { get; set; } = string.Empty;

    /// <summary>
    /// 内容模板
    /// </summary>
    [Required]
    public string ContentTemplate { get; set; } = null!;

    /// <summary>
    /// 默认布局名称
    /// </summary>
    [MaxLength(200)]
    public string? DefaultLayoutName { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 描述
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// 扩展元数据（JSON格式）
    /// </summary>
    [MaxLength(4000)]
    public string? Metadata { get; set; }
}

/// <summary>
/// 创建模板请求
/// </summary>
public class CreateTemplateRequest : TemplateRequestBase { }

/// <summary>
/// 更新模板请求
/// </summary>
public class UpdateTemplateRequest : TemplateRequestBase { }

/// <summary>
/// 查询模板请求
/// </summary>
public class QueryTemplateRequest : PagedQueryDto
{
    /// <summary>
    /// 默认每页数量
    /// </summary>
    protected override int DefaultPageSize => 20;

    /// <summary>
    /// 模块名称
    /// </summary>
    public string? Module { get; set; }

    /// <summary>
    /// 分类
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool? IsActive { get; set; }

    /// <summary>
    /// 关键词（搜索模板名称或描述）
    /// </summary>
    public string? Keyword { get; set; }
}

/// <summary>
/// 模板信息DTO（分页列表项）
/// </summary>
public class TemplateInfoDto
{
    public Guid Id { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string SubjectTemplate { get; set; } = string.Empty;
    public string ContentTemplate { get; set; } = string.Empty;
    public string? DefaultLayoutName { get; set; }
    public bool IsActive { get; set; }
    public string? Description { get; set; }
    public string? Metadata { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// 验证模板请求
/// </summary>
public class ValidateTemplateRequest
{
    /// <summary>
    /// 模板内容
    /// </summary>
    [Required]
    public string Content { get; set; } = null!;
}

/// <summary>
/// 预览模板请求
/// </summary>
public class PreviewTemplateRequest
{
    /// <summary>
    /// 模板内容
    /// </summary>
    [Required]
    public string Content { get; set; } = null!;

    /// <summary>
    /// 布局内容（可选）
    /// </summary>
    public string? LayoutContent { get; set; }

    /// <summary>
    /// 模板模型数据
    /// </summary>
    public Dictionary<string, object>? Model { get; set; }
}
