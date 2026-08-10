namespace Tnzi.Signing.Entities;

/// <summary>
/// 需要在一次 <see cref="Envelope"/> 上动作的一方。
/// </summary>
/// <remarks>
/// <para>
/// 每人一条<b>一次性</b>链接，不需要门户、不需要注册、不需要登录 —— 签署方通常根本不是本系统的用户。
/// </para>
/// <para>
/// ★ <b>令牌只存哈希，绝不存明文。</b>明文只存在于那封邮件里的 URL 中。一份泄漏的数据库备份
/// 不该等同于一叠可用的签署链接；而且查找路径比对的是哈希，不是拿秘密去做等值查询。
/// </para>
/// </remarks>
public class Signer : FullAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属请求</summary>
    public Guid RequestId { get; set; }

    /// <summary>
    /// 角色，与模板字段的 <c>RecipientRole</c> 对应（如 <c>Client</c> / <c>Counterparty</c> /
    /// <c>Witness</c>）。它决定这个人被要求填哪些字段。
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>姓名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>邮箱</summary>
    public string? Email { get; set; }

    /// <summary>顺序签署时的位次（1 起）。</summary>
    public int Order { get; set; } = 1;

    /// <summary>
    /// 令牌的 SHA-256（只存哈希，明文只在签发那一次返回）。
    /// </summary>
    /// <remarks>
    /// ★ 草稿阶段为 <c>null</c>，不是空串：令牌在 <c>SendAsync</c> 才签发，
    /// 而 TokenHash 上那条唯一索引若见到多行空串会**在插入第二个收件人时就炸** ——
    /// 一份请求本来就该有不止一个签署人。null 被索引过滤器排除在外，正是"还没有令牌"
    /// 与"令牌是某个值"之间该有的区别。
    /// </remarks>
    public string? TokenHash { get; set; }

    /// <summary>状态</summary>
    public SigningRecipientStatus Status { get; set; } = SigningRecipientStatus.Pending;

    /// <summary>发出时间</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>首次查看时间</summary>
    public DateTime? ViewedAt { get; set; }

    /// <summary>签署时间</summary>
    public DateTime? SignedAt { get; set; }

    /// <summary>拒签时间</summary>
    public DateTime? DeclinedAt { get; set; }

    /// <summary>拒签原因</summary>
    public string? DeclineReason { get; set; }

    /// <summary>捕获到的签名图（data URL，手绘或键入）。</summary>
    public string? SignatureImage { get; set; }

    // ── 签署审计 ──────────────────────────────────────────────────────────
    // 多数法域的电子签名法看重的是**归属**与**完整性**，而不是某张证书。
    // 下面三项随成品一起进完成证书，正是为了回答"凭什么说这是他签的"。

    /// <summary>签署者 IP</summary>
    public string? SignerIp { get; set; }

    /// <summary>签署者 User-Agent</summary>
    public string? SignerUserAgent { get; set; }

    /// <summary>签署者当时同意的条款原文（快照，不是指向某个会改的页面的链接）。</summary>
    public string? ConsentText { get; set; }

    /// <summary>所属请求</summary>
    public virtual Envelope? Request { get; set; }
}
