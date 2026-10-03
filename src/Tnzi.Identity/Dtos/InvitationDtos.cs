namespace Tnzi.Identity.Dtos;

/// <summary>
/// 发出一张邀请：开好账号、预设角色、签发一次性链接。
/// </summary>
/// <remarks>
/// ★ 用户名<b>由管理员决定</b>，不是被邀请人自己取。
/// 这与业界同类产品一致（AWS IAM Identity Center 的邀请邮件里直接印着
/// <c>Your Username</c>）：内部系统的账号命名通常有规矩（工号、企业邮箱），
/// 让新人自己取只会得到一堆需要事后纠正的名字。
/// </remarks>
public class CreateInvitationDto
{
    /// <summary>
    /// 登录用户名。<c>Identity:SignIn:UseEmailAsUserName</c> 开启（默认）且给了邮箱时<b>省略</b>，
    /// 用户名就是邮箱；给一个不同的值会被拒绝。关闭该开关或只有手机号时必填。
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>邮箱。邀请链接默认发到这里。</summary>
    [EmailAddress]
    public string? Email { get; set; }

    /// <summary>手机号。没有邮箱时用它接收邀请。</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>预设角色。被邀请人接受后立即生效。</summary>
    public List<Guid>? RoleIds { get; set; }

    /// <summary>所属组织。</summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>
    /// 管理员先行填好的资料，接受时作为表单初值交给消费应用。
    /// </summary>
    /// <remarks>
    /// 框架<b>不解释</b>这里的内容，原样存进邀请载荷、原样交给
    /// <see cref="Services.IInvitationAcceptanceHandler"/>。字段叫什么、有几个，
    /// 是消费应用的事。
    /// </remarks>
    public JsonElement? Profile { get; set; }

    /// <summary>
    /// 有效期（小时）。不给则用 <c>Identity:Invitation:LinkLifetimeHours</c>（默认 7 天）。
    /// </summary>
    public int? LifetimeHours { get; set; }
}

/// <summary>
/// 一张刚签发出来的邀请。
/// </summary>
/// <remarks>
/// ★ <see cref="AcceptUrl"/> 只在**这一次**返回，此后不可再读（库里只有令牌的哈希）。
/// 这也是「不用框架发信、自己拿链接去发」那条路的入口：拿到它，
/// 用企业微信、钉钉、打印出来当面给，都可以。
/// </remarks>
public class InvitationDto
{
    /// <summary>被邀请的账号。</summary>
    public Guid UserId { get; set; }

    /// <summary>登录用户名（邀请邮件里要原样告诉本人）。</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>接受邀请的完整链接。<b>仅此一次可读。</b></summary>
    public string AcceptUrl { get; set; } = string.Empty;

    /// <summary>链接失效时刻（UTC）。</summary>
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// 被邀请人打开链接时看到的东西。**匿名可达**，因此刻意给得很少。
/// </summary>
/// <remarks>
/// ★★ <strong>邮箱是掩码的。</strong>一条邀请链接如果能换出完整邮箱，
/// 那么链接泄露（转发、抄送、邮件网关日志）就等于员工邮箱泄露。
/// 掩码后本人仍认得出「是发给我的那封」，旁人拿不到可用信息。
/// </remarks>
public class InvitationPreviewDto
{
    /// <summary>登录用户名。这个要给全，本人此后要用它登录。</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>掩码后的邮箱，如 <c>t***@e***.com</c>；没有邮箱则为 null。</summary>
    public string? MaskedEmail { get; set; }

    /// <summary>掩码后的手机号，如 <c>138****8000</c>；没有则为 null。</summary>
    public string? MaskedPhoneNumber { get; set; }

    /// <summary>链接失效时刻（UTC）。</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// 管理员先行填好的资料，供前端预填表单。
    /// </summary>
    public JsonElement? Profile { get; set; }
}

/// <summary>
/// 接受邀请。
/// </summary>
public class AcceptInvitationDto
{
    /// <summary>邀请令牌（从链接里来）。</summary>
    [Required]
    public string Token { get; set; } = null!;

    /// <summary>
    /// 要设置的密码。是否必填由消费应用的
    /// <see cref="Services.IInvitationAcceptanceHandler"/> 决定 ——
    /// 只用 passkey 或只用单点登录的部署可以完全不要密码。
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// 消费应用自定义的表单载荷。框架原样透传，不解释其中任何字段。
    /// </summary>
    public JsonElement? Payload { get; set; }
}

/// <summary>
/// 接受邀请的结果。
/// </summary>
/// <remarks>
/// 实现 <see cref="IRefreshTokenCarrier"/> 并转发给嵌套的 <see cref="Token"/>，理由见
/// <see cref="PendingActionResultDto"/>：cookie 模式下的交付过滤器只看最外层载荷的形状。
/// </remarks>
public class AcceptInvitationResultDto : IRefreshTokenCarrier
{
    /// <summary>是否已全部完成。为 false 时账号仍未激活，见 <see cref="RemainingSteps"/>。</summary>
    public bool Completed { get; set; }

    /// <summary>
    /// 还差哪些步骤（如 <c>"EnrollTotp"</c>）。由消费应用定义字符串含义，框架原样传给前端。
    /// </summary>
    public IReadOnlyList<string>? RemainingSteps { get; set; }

    /// <summary>
    /// 完成且配置允许时直接给出的登录令牌；未完成、或
    /// <c>Identity:Invitation:SignInAfterAccept</c> 为 false 时为 null。
    /// </summary>
    public TokenResult? Token { get; set; }

    /// <inheritdoc />
    string? IRefreshTokenCarrier.ReadRefreshToken() => ((IRefreshTokenCarrier?)Token)?.ReadRefreshToken();

    /// <inheritdoc />
    int? IRefreshTokenCarrier.ReadRefreshTokenLifetimeSeconds() => ((IRefreshTokenCarrier?)Token)?.ReadRefreshTokenLifetimeSeconds();

    /// <inheritdoc />
    void IRefreshTokenCarrier.ClearRefreshToken() => ((IRefreshTokenCarrier?)Token)?.ClearRefreshToken();
}
