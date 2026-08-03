namespace Tnzi.Documents.Signing.Services;

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
public interface IEnvelopeService
{
    /// <summary>
    /// 从模板发起一份请求（落 <c>Draft</c>）。此刻就把模板与字段冻结成快照。
    /// </summary>
    Task<Result<EnvelopeDto>> CreateAsync(CreateEnvelopeDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发出：渲染合并稿、给每个收件人签发一次性令牌、推进到 <c>Sent</c>。
    /// </summary>
    /// <returns>
    /// 每个收件人的<b>明文令牌</b>，调用方据此拼签署链接发出去。
    /// ★ 明文只在这一次返回，之后库里只有哈希 —— 补发链接要重新签发令牌。
    /// </returns>
    Task<Result<IReadOnlyList<IssuedSigningLink>>> SendAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>按令牌取件（收件人视角）。顺序签署时未轮到者会被告知在排队。</summary>
    Task<Result<SigningPacketDto>> GetByTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按令牌提交本人负责的字段与签名。全部收件人签完时自动密封并归档。
    /// </summary>
    Task<Result<SigningPacketDto>> SubmitAsync(string token, SubmitSigningDto input, CancellationToken cancellationToken = default);

    /// <summary>按令牌拒签。一人拒签即整份请求作废（<c>Declined</c>）。</summary>
    Task<Result<SigningPacketDto>> DeclineAsync(string token, string? reason, CancellationToken cancellationToken = default);

    /// <summary>管理端作废一份尚未完成的请求。</summary>
    Task<Result> VoidAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>取一份请求的管理端视图。</summary>
    Task<Result<EnvelopeDto>> GetAsync(Guid requestId, CancellationToken cancellationToken = default);
}

/// <summary>一条刚签发出来的签署链接凭据。</summary>
/// <param name="RecipientId">收件人</param>
/// <param name="Name">姓名</param>
/// <param name="Email">邮箱</param>
/// <param name="Token">★ 明文令牌，只在签发这一刻存在于内存里</param>
public sealed record IssuedSigningLink(Guid RecipientId, string Name, string? Email, string Token);
