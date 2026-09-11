namespace Tnzi.Identity.Services;

/// <summary>
/// 邀请注册：管理员开好账号并预设角色，被邀请人凭一条一次性链接补齐信息、激活账号。
/// </summary>
/// <remarks>
/// <para>
/// <strong>面向的是不开放注册的系统</strong>（公司内部运营后台之类）：人是先在组织里存在、
/// 再有账号的，而不是自己找上门来注册。
/// </para>
/// <para>
/// ★★★ <strong>账号在发出邀请的那一刻就已经建好了</strong>，只是处于
/// <see cref="PendingUserActions.InvitationPending"/>。这与 Microsoft Entra（建 Guest 用户，
/// 状态 <c>PendingAcceptance</c>）、Okta（STAGED / PROVISIONED / ACTIVE）、
/// AWS IAM Identity Center 的选择一致。好处是直接的：
/// <list type="bullet">
/// <item>用户名在邀请那一刻就定下来（内部系统的账号命名通常有规矩，不能让新人自己取）</item>
/// <item>「谁还没接受邀请」就是用户列表的一个筛选项，不需要单独的邀请列表</item>
/// <item>撤销邀请就是删账号，不需要第二套生命周期</item>
/// <item>角色预设是真实的角色记录，不是一份等着被解释的草稿</item>
/// </list>
/// </para>
/// <para>
/// ★★ <strong>代价是必须有人守住「未激活的账号不能登录」</strong>，而且要守住<b>全部</b>
/// 签发路径而不只是密码登录 —— 这件事由 <see cref="PendingActionsLoginGuard"/> 负责，
/// 它挂在 <see cref="ILoginGuardEvaluator"/> 上，那是所有签发路径的唯一共同调用点。
/// 另有两条不签发令牌、因而守卫够不着的路径单独堵在了源头：
/// <c>PasswordService.ForgotPasswordAsync</c>（否则任何人都能替未入职的账号设密码）与
/// <c>UserService.EnableAsync</c>（否则管理员点一下「启用」就把人放进来了）。
/// </para>
/// <para>
/// ★ <strong>令牌复用 <c>AuthToken</c></strong>（<c>LoginProvider = "Invitation"</c>、
/// <c>Name = "InvitationToken"</c>），零迁移：唯一索引
/// <c>(UserId, LoginProvider, Name, SessionId)</c> 天然给出「每人至多一枚有效邀请、
/// 重发即作废上一枚」，并自动继承既有的过期清理后台任务与 <c>[AuditIgnore]</c> 豁免。
/// 只存哈希不存明文，且刻意不加盐不慢哈希（理由见 <see cref="OneTimeToken"/>）。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "接受流程的分步形态仍可能调整")]
public interface IInvitationService
{
    /// <summary>
    /// 开一个账号并发出邀请。
    /// </summary>
    /// <remarks>
    /// 账号经 <c>IUserService.CreateAsync</c> 创建 —— 走那条路而不是自己拼一个
    /// <c>User</c>，是因为重名校验、密码策略与注册事件只在那一条路上。
    /// 创建出来的账号没有密码，且同时被置上 <see cref="PendingUserActions.InvitationPending"/> 与账号锁定。
    /// </remarks>
    Task<Result<InvitationDto>> InviteAsync(CreateInvitationDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 给一个已存在的未激活账号重发邀请，<b>上一条链接立即失效</b>。
    /// </summary>
    /// <remarks>
    /// 只对 <see cref="PendingUserActions.InvitationPending"/> 的账号有效。
    /// 对已激活的账号重发没有意义（那是「找回密码」要解决的问题），返回 409。
    /// </remarks>
    Task<Result<InvitationDto>> ResendAsync(Guid userId, int? lifetimeHours = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 撤销邀请：软删账号，链接随之失效。
    /// </summary>
    /// <remarks>
    /// 发错了人、员工入职前反悔，都走这里。软删不会挡住此后用同一个用户名/邮箱重新邀请 ——
    /// 用户表的唯一索引带 <c>IsDeleted</c> 过滤器。
    /// </remarks>
    Task<Result> RevokeAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按令牌取邀请的展示信息。**不消费令牌**，供接受页打开时渲染。
    /// </summary>
    /// <remarks>邮箱与手机号是掩码的，理由见 <see cref="InvitationPreviewDto"/>。</remarks>
    Task<Result<InvitationPreviewDto>> PreviewAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// 接受邀请。
    /// </summary>
    /// <remarks>
    /// 编排顺序是这套机制的安全边界，不能调换：校验令牌 → 交给
    /// <see cref="IInvitationAcceptanceHandler"/> → <b>它说完成了</b>才抢占状态、解锁、
    /// 消费令牌 → 按配置签发登录令牌（仍然过登录守卫）。
    /// </remarks>
    Task<Result<AcceptInvitationResultDto>> AcceptAsync(AcceptInvitationDto input, CancellationToken cancellationToken = default);
}
