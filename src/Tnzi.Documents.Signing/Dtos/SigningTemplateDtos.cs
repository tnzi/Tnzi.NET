namespace Tnzi.Documents.Signing.Dtos;

/// <summary>模板列表项（不含字段——列表页要的是"有哪些模板"，不是每个模板的每个字段）。</summary>
public class EnvelopeTemplateListDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public TemplateSource Source { get; set; }
    public int PageCount { get; set; }
    public int FieldCount { get; set; }
    public bool RequiresWetSignature { get; set; }
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>模板详情（含字段）。</summary>
public class EnvelopeTemplateDto : EnvelopeTemplateListDto
{
    public string? HostEntityTypes { get; set; }
    public string BodyTemplate { get; set; } = string.Empty;
    public Guid? SourceFileId { get; set; }
    public string? SourceFileName { get; set; }
    public Guid? RenderedPdfFileId { get; set; }
    public List<TemplateFieldDto> Fields { get; set; } = [];
}

/// <summary>模板设计面上的一个字段（与收件人面的 <see cref="RecipientFieldDto"/> 不同：
/// 那个说的是"签的人看到什么"，这个说的是"它落在页面哪里、绑什么、谁来填"）。</summary>
public class TemplateFieldDto
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public SigningFieldType Type { get; set; }
    public string? RecipientRole { get; set; }
    public string? Binding { get; set; }
    public bool Required { get; set; }
    public FieldPlacementMode PlacementMode { get; set; }
    public string? AnchorText { get; set; }
    public int Page { get; set; }
    public decimal X { get; set; }
    public decimal Y { get; set; }
    public decimal W { get; set; }
    public decimal H { get; set; }
    public decimal? FontSize { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>新建模板。</summary>
public class CreateEnvelopeTemplateDto
{
    public string Name { get; set; } = null!;

    /// <summary>分组键，消费应用自定（框架不内置分类表）</summary>
    public string? Category { get; set; }

    public TemplateSource Source { get; set; }

    /// <summary>可用于哪些宿主类型（逗号分隔）；空 = 不限</summary>
    public string? HostEntityTypes { get; set; }

    /// <summary>正文模板（<see cref="TemplateSource.Composed"/> 用，含 <c>{{变量}}</c>）</summary>
    public string? BodyTemplate { get; set; }

    /// <summary>上传的原件（<see cref="TemplateSource.Uploaded"/> 用）</summary>
    public Guid? SourceFileId { get; set; }

    public string? SourceFileName { get; set; }

    /// <summary>渲染稿（PDF）。Uploaded 且原件已是 PDF 时与 <see cref="SourceFileId"/> 相同。</summary>
    public Guid? RenderedPdfFileId { get; set; }

    public int PageCount { get; set; } = 1;

    public bool RequiresWetSignature { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>字段（内嵌全量）</summary>
    public List<TemplateFieldInputDto> Fields { get; set; } = [];
}

/// <summary>更新模板（字段整体重建）。</summary>
public class UpdateEnvelopeTemplateDto : CreateEnvelopeTemplateDto;

/// <summary>字段输入。</summary>
public class TemplateFieldInputDto
{
    public string Key { get; set; } = null!;
    public string? Label { get; set; }
    public SigningFieldType Type { get; set; }

    /// <summary>由哪个角色填；null = 发起方预填</summary>
    public string? RecipientRole { get; set; }

    /// <summary>绑定的合并变量键</summary>
    public string? Binding { get; set; }

    public bool Required { get; set; }
    public FieldPlacementMode PlacementMode { get; set; }
    public string? AnchorText { get; set; }
    public int Page { get; set; } = 1;

    // 归一化 0-1、左上角原点（与 Tnzi.Documents 同口径）
    public decimal X { get; set; }
    public decimal Y { get; set; }
    public decimal W { get; set; }
    public decimal H { get; set; }

    public decimal? FontSize { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>模板查询。</summary>
public class EnvelopeTemplateQueryDto : PagedQueryDto
{
    public string? Keyword { get; set; }
    public string? Category { get; set; }
    public TemplateSource? Source { get; set; }

    /// <summary>按可用宿主类型过滤（匹配 <c>HostEntityTypes</c> 里的任一项，或不限的模板）</summary>
    public string? HostEntityType { get; set; }

    public bool? IsActive { get; set; }
}
