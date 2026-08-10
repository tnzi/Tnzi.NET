namespace Tnzi.Signing.Dtos;

/// <summary>发起一份签署请求。</summary>
public class CreateEnvelopeDto
{
    /// <summary>来源模板</summary>
    public Guid TemplateId { get; set; }

    /// <summary>标题；留空时取模板名</summary>
    public string? Title { get; set; }

    /// <summary>宿主类型名；留空 = 不绑定任何业务记录的独立文档</summary>
    public string? HostEntityType { get; set; }

    /// <summary>宿主记录 id</summary>
    public Guid? HostEntityId { get; set; }

    /// <summary>是否顺序签署（默认 true）</summary>
    public bool IsSequential { get; set; } = true;

    /// <summary>有效期（天）；默认 30</summary>
    public int ExpiresInDays { get; set; } = 30;

    /// <summary>收件人（顺序签署时按列表顺序定位次）</summary>
    public List<CreateSignerDto> Recipients { get; set; } = null!;

    /// <summary>
    /// 发起方预填的字段值（键 = 字段 Key）。绑定了合并变量的字段无需在此给出。
    /// </summary>
    public Dictionary<string, string?>? PrefilledValues { get; set; }
}

/// <summary>一个收件人。</summary>
public class CreateSignerDto
{
    /// <summary>角色，决定这个人被要求填哪些字段</summary>
    public string Role { get; set; } = null!;

    /// <summary>姓名</summary>
    public string Name { get; set; } = null!;

    /// <summary>邮箱</summary>
    public string? Email { get; set; }
}

/// <summary>
/// 列表项（不含收件人明细 —— 列表页要回答的是"这份签到哪一步了"，不是"每个人分别怎么样"）。
/// </summary>
public class EnvelopeListDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? HostEntityType { get; set; }
    public Guid? HostEntityId { get; set; }
    public Guid? TemplateId { get; set; }

    /// <summary>
    /// 此刻的真实状态。
    /// </summary>
    /// <remarks>
    /// 过期是<b>算出来</b>的而不是库里存的（见 <c>EnvelopeExpiry</c>）：没有任何人的动作会把一份
    /// 请求变成过期，只是时间到了。所以这里可能返回 <see cref="EnvelopeStatus.Expired"/>，
    /// 而库里那一行仍是 <c>Sent</c>。
    /// </remarks>
    public EnvelopeStatus Status { get; set; }

    public bool IsSequential { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreationTime { get; set; }

    /// <summary>收件人总数</summary>
    public int RecipientCount { get; set; }

    /// <summary>已签人数（进度条的分子）</summary>
    public int SignedCount { get; set; }

    /// <summary>成品已密封时非 null</summary>
    public Guid? FinalPdfFileId { get; set; }
}

/// <summary>请求查询。</summary>
public class EnvelopeQueryDto : PagedQueryDto
{
    /// <summary>按标题模糊匹配</summary>
    public string? Keyword { get; set; }

    /// <summary>
    /// 按状态筛选；<see cref="EnvelopeStatus.Expired"/> 走的是同一条派生规则，
    /// 因此筛出来的和列表里标着已过期的必然是同一批。
    /// </summary>
    public EnvelopeStatus? Status { get; set; }

    /// <summary>按宿主类型（业务模块名）筛选</summary>
    public string? HostEntityType { get; set; }

    /// <summary>按宿主记录筛选（"这条合同记录上挂了哪些签署请求"）</summary>
    public Guid? HostEntityId { get; set; }

    /// <summary>按来源模板筛选</summary>
    public Guid? TemplateId { get; set; }
}

/// <summary>管理端看到的一份请求。</summary>
public class EnvelopeDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? HostEntityType { get; set; }
    public Guid? HostEntityId { get; set; }

    /// <inheritdoc cref="EnvelopeListDto.Status" />
    public EnvelopeStatus Status { get; set; }

    public bool IsSequential { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>密封成品；未完成时为 null</summary>
    public Guid? FinalPdfFileId { get; set; }

    /// <summary>成品的 SHA-256（防篡改锚点）</summary>
    public string? Sha256 { get; set; }

    /// <summary>
    /// 完成证书（独立 PDF，记述谁在什么时候从哪里签的，并写有上面那个哈希）。
    /// 已完成但此项为 null = 证书生成失败，成品本身仍然有效。
    /// </summary>
    public Guid? CompletionCertificateFileId { get; set; }

    public List<SignerDto> Recipients { get; set; } = [];
}

/// <summary>管理端看到的一个收件人。</summary>
public class SignerDto
{
    public Guid Id { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public int Order { get; set; }
    public SigningRecipientStatus Status { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? ViewedAt { get; set; }
    public DateTime? SignedAt { get; set; }
    public DateTime? DeclinedAt { get; set; }
    public string? DeclineReason { get; set; }
}

/// <summary>
/// 收件人打开链接时看到的东西。
/// </summary>
/// <remarks>
/// 刻意<b>不含</b>其他收件人的邮箱、令牌、宿主记录 id 之类 —— 这份载荷发给的是一个匿名访客，
/// 他只需要知道自己该做什么。
/// </remarks>
public class SigningPacketDto
{
    /// <summary>文档标题</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>本人姓名（让他确认"这是给我的"）</summary>
    public string RecipientName { get; set; } = string.Empty;

    /// <summary>本人状态</summary>
    public SigningRecipientStatus RecipientStatus { get; set; }

    /// <summary>整份请求的状态</summary>
    public EnvelopeStatus RequestStatus { get; set; }

    /// <summary>是否轮到本人（顺序签署时前面还有人没签则为 false）</summary>
    public bool IsMyTurn { get; set; }

    /// <summary>待本人填写的字段</summary>
    public List<RecipientFieldDto> Fields { get; set; } = [];

    /// <summary>可供预览的 PDF 文件 id（渲染稿；完成后是密封成品）</summary>
    public Guid? DocumentFileId { get; set; }

    /// <summary>过期时间</summary>
    public DateTime ExpiresAt { get; set; }
}

/// <summary>待填字段（收件人视角）。</summary>
public class RecipientFieldDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public SigningFieldType Type { get; set; }
    public bool Required { get; set; }

    /// <summary>已有的值（发起方预填或合并变量带入）</summary>
    public string? Value { get; set; }
}

/// <summary>收件人提交。</summary>
public class SubmitSigningDto
{
    /// <summary>字段取值（键 = 字段 Key）</summary>
    public Dictionary<string, string?>? Values { get; set; }

    /// <summary>签名图（data URL）</summary>
    public string? SignatureImage { get; set; }

    /// <summary>
    /// 签署人当时同意的条款原文。
    /// </summary>
    /// <remarks>
    /// 存的是<b>原文快照</b>而不是指向某个会改的页面的链接 —— 事后要能回答"他当时同意的是什么"。
    /// </remarks>
    public string? ConsentText { get; set; }
}
