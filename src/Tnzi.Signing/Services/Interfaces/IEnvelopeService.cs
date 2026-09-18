namespace Tnzi.Signing.Services;

/// <summary>
/// 签署请求的全生命周期：发起 → 发出 → 逐人签署 → 密封归档。
/// </summary>
/// <remarks>
/// <para>
/// 按令牌的那几个方法（<see cref="GetByTokenAsync"/> / <see cref="SubmitAsync"/> /
/// <see cref="DeclineAsync"/>）服务的是<b>匿名收件人</b>：签署方通常根本不是本系统的用户，
/// 身份完全由令牌担保。它们绝不接受收件人 id 之类的参数 —— 那等于把"我是谁"交给调用方决定。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "E-signature contracts are shaped by a single consumer so far; they may change before a second one validates them")]
public interface IEnvelopeService
{
    /// <summary>
    /// 从模板发起一份请求（落 <c>Draft</c>）。此刻就把模板与字段冻结成快照。
    /// </summary>
    Task<Result<EnvelopeDto>> CreateAsync(CreateEnvelopeDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发出：校验发起方负责的必填字段都有值、把这些值烧进一份本信封自有的渲染稿（渲染合并稿）、
    /// 给每个收件人签发一次性令牌、推进到 <c>Sent</c>。
    /// </summary>
    /// <returns>
    /// 每个收件人的<b>明文令牌</b>，调用方据此拼签署链接发出去。
    /// ★ 明文只在这一次返回，之后库里只有哈希 —— 补发链接要重新签发令牌。
    /// </returns>
    Task<Result<IReadOnlyList<IssuedSigningLink>>> SendAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按令牌取件（收件人视角）。顺序签署时未轮到者会被告知在排队。
    /// 作废 / 拒签 / 过期的请求只回状态（<c>Fields</c> 为空、<c>DocumentFileId</c> 为 null，不记查看时间），
    /// 好让消费方的签署页能说「这份请求已取消」；作废的链接已被吊销，答 404。
    /// </summary>
    Task<Result<SigningPacketDto>> GetByTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按令牌取回收件人正在签（或已签成）的那份 PDF 的字节：完成前是渲染稿，完成后是密封成品。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 收件人是匿名的，而 <c>Tnzi.Storage</c> 的读取判定对匿名一律 404 —— 所以
    /// <see cref="SigningPacketDto.DocumentFileId"/> 对他而言只是一个打不开的 id。
    /// 这条路径把令牌校验的结论写进请求级授予（<c>IFileAccessGrantContext</c>），
    /// 与分享链接同一形状：令牌本身就是凭据，授予只在这一次请求内、只给读。
    /// </para>
    /// <para>
    /// 读取也看状态：进行中（未过期）给渲染稿，已完成给密封成品；作废 / 拒签 / 过期的请求答 404，
    /// 作废还会吊销令牌。一条本该失效的链接不该永远保有读权限，一份发错人的请求要收得回来。
    /// </para>
    /// </remarks>
    Task<Result<SigningDocumentContent>> GetDocumentByTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按令牌提交本人负责的字段与签名。全部收件人签完时自动密封并归档。
    /// </summary>
    Task<Result<SigningPacketDto>> SubmitAsync(string token, SubmitSigningDto input, CancellationToken cancellationToken = default);

    /// <summary>按令牌拒签。一人拒签即整份请求作废（<c>Declined</c>）。</summary>
    Task<Result<SigningPacketDto>> DeclineAsync(string token, string? reason, CancellationToken cancellationToken = default);

    /// <summary>管理端作废一份尚未完成的请求，并吊销每个收件人的签署链接（<c>TokenHash</c> 清空）。</summary>
    Task<Result> VoidAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 管理端重新密封：收件人已全部签完、而最后那次提交里的密封失败（请求停在 <c>InProgress</c>）时，
    /// 再跑一遍密封 → 完成证书 → 归档。
    /// </summary>
    /// <remarks>
    /// 密封在最后一位收件人的那次提交里进行，一次瞬时故障（存储超时、盖章异常）就会让它失败；
    /// 那时收件人已全部 <c>Signed</c> 不能重交，没有这条路就只剩作废重发、作废每一个已收集的签名。
    /// 已密封的请求拒绝再密封（409）：哈希只算一次。
    /// </remarks>
    Task<Result<EnvelopeDto>> SealAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>取一份请求的管理端视图。</summary>
    Task<Result<EnvelopeDto>> GetAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页列出请求（管理端）。
    /// </summary>
    /// <remarks>
    /// 返回的 <c>Status</c> 是<b>按 ExpiresAt 现算</b>的，可能与库里那一行不同 ——
    /// 过期不落库的理由见 <c>EnvelopeExpiry</c>。
    /// </remarks>
    Task<Result<IPagedList<EnvelopeListDto>>> GetPagedAsync(
        EnvelopeQueryDto query, CancellationToken cancellationToken = default);
}

/// <summary>收件人按令牌取回的文档字节。流归调用方释放。</summary>
/// <param name="Content">PDF 字节流</param>
/// <param name="ContentType">内容类型（正常情况下是 <c>application/pdf</c>）</param>
/// <param name="FileName">下载时的展示名</param>
public sealed record SigningDocumentContent(Stream Content, string ContentType, string FileName);

/// <summary>一条刚签发出来的签署链接凭据。</summary>
/// <param name="RecipientId">收件人</param>
/// <param name="Name">姓名</param>
/// <param name="Email">邮箱</param>
/// <param name="Token">★ 明文令牌，只在签发这一刻存在于内存里</param>
/// <param name="TenantId">
/// 请求所属的租户（多租户未开启时为 null）。匿名端点自己会按令牌切进这个租户，消费方不必传；
/// 给出来是让多租户宿主的签署页能把自己的其它请求（不走本模块的）挂在同一个租户上。
/// </param>
public sealed record IssuedSigningLink(Guid RecipientId, string Name, string? Email, string Token, Guid? TenantId = null);
