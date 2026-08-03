namespace Tnzi.Documents.Signing.Entities;

/// <summary>
/// 一次签署请求（相当于 DocuSign 的"信封"）。
/// </summary>
/// <remarks>
/// <para>
/// <b>自带模板快照。</b><see cref="TemplateSnapshotJson"/> 是发出那一刻模板与其字段的冻结副本。
/// 权威内容是这份快照，<b>不是</b>活的模板 —— 否则中途修订模板会改掉签署人正在看的东西，
/// 甚至挪动签名落笔的位置，而这两件事都会让已经收集到的签名失去意义。
/// </para>
/// <para>
/// <b>宿主记录是多态引用</b>（<see cref="HostEntityType"/> + <see cref="HostEntityId"/>）而不是
/// 类型化外键，这正是本模块可以不依赖任何业务模块的原因。成品经
/// <see cref="Services.IDocumentHostSink"/> 交回给拥有那条记录的模块。
/// </para>
/// </remarks>
public class Envelope : FullAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>宿主类型名；<c>null</c> = 不绑定任何记录的独立文档。</summary>
    public string? HostEntityType { get; set; }

    /// <summary>宿主记录 id。</summary>
    public Guid? HostEntityId { get; set; }

    /// <summary>发起自哪个模板。仅供统计与追溯，权威内容见 <see cref="TemplateSnapshotJson"/>。</summary>
    public Guid? TemplateId { get; set; }

    /// <summary>标题</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>发出时模板与字段的冻结副本（JSON）。</summary>
    public string TemplateSnapshotJson { get; set; } = string.Empty;

    /// <summary>发出时的 PDF：合并变量已烧进去，签名框还空着。</summary>
    [FileField]
    public Guid? RenderedPdfFileId { get; set; }

    /// <summary>密封后的 PDF：全部字段已盖章并压平。完成时一次性写入。</summary>
    [FileField]
    public Guid? FinalPdfFileId { get; set; }

    /// <summary>完成证书（谁在什么时候、从哪个 IP 签的，以及文档哈希）。</summary>
    [FileField]
    public Guid? CompletionCertificateFileId { get; set; }

    /// <summary>
    /// <see cref="FinalPdfFileId"/> 字节的 SHA-256（十六进制）—— 防篡改的锚点。
    /// </summary>
    /// <remarks>
    /// 事后争议时，能证明"这份 PDF 就是当初签的那份"的，是这个值与完成证书的对应关系。
    /// 它必须在密封那一刻算，而不是在任何"重新生成"的时候。
    /// </remarks>
    public string? Sha256 { get; set; }

    /// <summary>
    /// true = 收件人按 <see cref="Signer.Order"/> 依次签；false = 所有人可同时签。
    /// </summary>
    public bool IsSequential { get; set; } = true;

    /// <summary>状态</summary>
    public EnvelopeStatus Status { get; set; } = EnvelopeStatus.Draft;

    /// <summary>过期时间</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>完成时间</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>发起人（去规范化，便于列表展示与归档后仍可读）</summary>
    public Guid? SentByUserId { get; set; }

    /// <summary>发起人姓名</summary>
    public string? SentByName { get; set; }

    /// <summary>收件人</summary>
    public virtual ICollection<Signer> Recipients { get; set; } = new List<Signer>();

    /// <summary>字段取值</summary>
    public virtual ICollection<FieldValue> FieldValues { get; set; } = new List<FieldValue>();
}
