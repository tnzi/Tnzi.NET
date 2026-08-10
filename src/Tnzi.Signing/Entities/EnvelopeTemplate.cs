namespace Tnzi.Signing.Entities;

/// <summary>
/// 可复用的签署模板：页面内容 + 放置好的字段。
/// </summary>
public class EnvelopeTemplate : FullAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>分类（消费应用自定的分组键，框架不内置分类表）</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>内容来源</summary>
    public TemplateSource Source { get; set; }

    /// <summary>
    /// 可用于哪些宿主类型（逗号分隔）。空 = 不限。
    /// </summary>
    /// <remarks>
    /// 存的是 <see cref="Services.IMergeSourceProvider.EntityType"/> 那些字符串；
    /// 模板设计器据此决定给出哪些合并变量。
    /// </remarks>
    public string? HostEntityTypes { get; set; }

    /// <summary>正文模板（<see cref="TemplateSource.Composed"/> 时使用，含 <c>{{变量}}</c>）。</summary>
    public string BodyTemplate { get; set; } = string.Empty;

    /// <summary>上传的原始文件（<see cref="TemplateSource.Uploaded"/> 时使用）。</summary>
    [FileField]
    public Guid? SourceFileId { get; set; }

    /// <summary>上传文件名</summary>
    public string? SourceFileName { get; set; }

    /// <summary>渲染后的 PDF（DOCX 在上传时一次性转换，之后一切都对着 PDF 来）。</summary>
    [FileField]
    public Guid? RenderedPdfFileId { get; set; }

    /// <summary>页数</summary>
    public int PageCount { get; set; }

    /// <summary>
    /// 是否要求湿签（纸笔）。为真时本模板不参与电子签署流程，只用于打印。
    /// </summary>
    /// <remarks>
    /// 有些文书按法域要求必须手写签名。让它成为模板上的一个事实，好过让每个使用者
    /// 各自记住"这份不能走电子签"。
    /// </remarks>
    public bool RequiresWetSignature { get; set; }

    /// <summary>是否启用</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>版本号（每次改动递增，供请求快照标注来源版本）</summary>
    public int Version { get; set; } = 1;

    /// <summary>字段</summary>
    public virtual ICollection<Field> Fields { get; set; } = new List<Field>();
}

/// <summary>
/// 模板上的一个待填字段：填什么、谁填、落在页面哪里。
/// </summary>
/// <remarks>
/// ★ <b>硬删</b>（与 <c>SalaryStructureLine</c> / <c>BracketRow</c> 同一先例），刻意不是软删：
/// <list type="number">
/// <item>字段集随模板<b>整体重建</b>。软删时「软删旧行」与「插同键新行」在同一次
/// <c>SaveChanges</c> 里，而 (TemplateId, Key) 上那条过滤唯一索引会因两者的执行顺序
/// 产生瞬时冲突 —— Payroll 为此不得不在中间插一次 flush。</item>
/// <item>模板在设计期会被反复编辑，软删会让每改一次就沉淀一批永不再读的行。</item>
/// <item>真正需要被审计留存的不是这里，而是<b>请求快照</b>（<c>TemplateSnapshotJson</c>）：
/// 那才是"这份文件当初照的是什么"的权威答案，而它与模板行没有任何外键关系。</item>
/// </list>
/// </remarks>
public class Field : EntityBase<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属模板</summary>
    public Guid TemplateId { get; set; }

    /// <summary>字段键（模板内唯一）</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>展示名</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>类型</summary>
    public SigningFieldType Type { get; set; }

    /// <summary>由哪个角色填写；<c>null</c> = 发起方预填。</summary>
    public string? RecipientRole { get; set; }

    /// <summary>
    /// 绑定到哪个合并变量（<see cref="Services.MergeFieldDescriptor.Key"/>）。
    /// 有绑定的字段在渲染时就填好，收件人看到的是已填内容。
    /// </summary>
    public string? Binding { get; set; }

    /// <summary>是否必填</summary>
    public bool Required { get; set; }

    /// <summary>定位方式</summary>
    public FieldPlacementMode PlacementMode { get; set; }

    /// <summary>锚文本（<see cref="FieldPlacementMode.Anchor"/> 时使用）</summary>
    public string? AnchorText { get; set; }

    /// <summary>页码（1 起）</summary>
    public int Page { get; set; } = 1;

    // ★ 坐标一律归一化到 0-1、左上角原点 —— 与 Tnzi.Documents 的 PDF 原语同一口径
    //   （读要翻 Y、写不翻 Y，那条约束在 Tnzi.Documents 里）。用归一化坐标而不是点，
    //   是因为同一份模板可能以不同纸张尺寸渲染，而字段该待在同一个相对位置上。

    /// <summary>左上角 X（归一化 0-1）</summary>
    public decimal X { get; set; }

    /// <summary>左上角 Y（归一化 0-1）</summary>
    public decimal Y { get; set; }

    /// <summary>宽（归一化 0-1）</summary>
    public decimal W { get; set; }

    /// <summary>高（归一化 0-1）</summary>
    public decimal H { get; set; }

    /// <summary>字号（磅）；null = 由渲染器按框高自适应</summary>
    public decimal? FontSize { get; set; }

    /// <summary>排序</summary>
    public int SortOrder { get; set; }

    /// <summary>所属模板</summary>
    public virtual EnvelopeTemplate? Template { get; set; }
}

/// <summary>
/// 一次请求里某个字段的实际取值。
/// </summary>
/// <remarks>
/// 值已经烧进 PDF 了，这里再存一份是为了让归档能回答"这份文档当初填的是什么"，
/// 而不必回头去解析 PDF。
/// </remarks>
public class FieldValue : FullAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属请求</summary>
    public Guid RequestId { get; set; }

    /// <summary>字段键（对应快照里的字段，不是外键 —— 模板可能已经改了）</summary>
    public string FieldKey { get; set; } = string.Empty;

    /// <summary>由哪个收件人填的；<c>null</c> = 发起方预填或合并变量带入。</summary>
    public Guid? RecipientId { get; set; }

    /// <summary>取值</summary>
    public string? Value { get; set; }

    /// <summary>所属请求</summary>
    public virtual Envelope? Request { get; set; }
}
