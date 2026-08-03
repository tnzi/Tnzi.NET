namespace Tnzi.Documents.Signing.Metadata;

/// <summary>
/// 模板页面内容的来源。两种来源最终都落成一份带归一化坐标字段的 PDF，
/// 所以下游（渲染、签署、盖章）与来源无关。
/// </summary>
public enum TemplateSource
{
    /// <summary>
    /// 由平台拼装（标题 + 有序条款，版式由框架排）。字段坐标在<b>渲染过程中</b>就地捕获，
    /// 因此不需要事后再按锚文本去搜。
    /// </summary>
    Composed = 0,

    /// <summary>
    /// 上传的 PDF 或 Word 文件。DOCX 在上传时一次性转成 PDF；字段坐标来自设计器或 <c>{{tag}}</c> 扫描。
    /// </summary>
    Uploaded = 1,
}

/// <summary>字段收集什么，以及它怎么落到页面上。</summary>
public enum SigningFieldType
{
    Text = 0,
    Date = 1,
    Number = 2,
    Checkbox = 3,

    /// <summary>手绘或键入的签名，按图片捕获。</summary>
    Signature = 4,

    /// <summary>签名缩写：与 <see cref="Signature"/> 同一条捕获路径，框更小。</summary>
    Initials = 5,
}

/// <summary>字段在渲染后的 PDF 上如何定位。</summary>
public enum FieldPlacementMode
{
    /// <summary>
    /// 固定归一化框（页码 + x/y/w/h，0-1，左上角原点）。设计器与 Composed 渲染器都产出这种。
    /// </summary>
    Absolute = 0,

    /// <summary>
    /// 按 <c>AnchorText</c> 在页面文本里搜索定位。只用于分页可能变动的上传文档。
    /// </summary>
    Anchor = 1,
}

/// <summary>
/// 一次签署请求的生命周期（相当于 DocuSign 的"信封"）。
/// </summary>
/// <remarks>
/// 只有<b>每一个</b>收件人都签完、且压平后的 PDF 已密封，请求才进入 <see cref="Completed"/>。
/// </remarks>
public enum EnvelopeStatus
{
    Draft = 0,

    /// <summary>已发出，还没有人动作。</summary>
    Sent = 1,

    /// <summary>至少一人已签，但没签完。</summary>
    InProgress = 2,

    Completed = 3,
    Declined = 4,
    Expired = 5,
    Voided = 6,
}

/// <summary>请求内单个收件人的状态。</summary>
public enum SigningRecipientStatus
{
    /// <summary>还没轮到（顺序签署时）。</summary>
    Pending = 0,

    Sent = 1,
    Viewed = 2,
    Signed = 3,
    Declined = 4,
}
