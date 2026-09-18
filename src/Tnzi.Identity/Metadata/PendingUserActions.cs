namespace Tnzi.Identity.Metadata;

/// <summary>
/// 账号身上还欠着的事：要么欠到**登录不进来**，要么欠到**进来了但先得办完**。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <strong>位的高低位置本身携带语义，不是随手编号。</strong>
/// 低位（<c>1 &lt;&lt; 0</c> 起）是<strong>阻断位</strong>：
/// <see cref="PendingActionsLoginGuard"/> 见到就否决签发，人根本进不来。
/// 高位（<c>1 &lt;&lt; 8</c> 起）是<strong>义务位</strong>：凭据已经过关、身份已经证明，
/// 只是必须先把这件事办完才能真正开始用系统（形态与 2FA 挑战相同）。
/// 中间刻意留空，新增一个位时人**必须先决定它属于哪一半**，而位置就是那个决定的记录。
/// </para>
/// <para>
/// ★★ 分类的完整性由 <c>PendingUserActionsTests</c> 守着：任何已定义的位都必须落在
/// <see cref="Blocking"/> 或 <see cref="Obligations"/> 里。少了那条门禁，
/// 加一个位却忘了归类的后果是**它静默地什么都不做** —— 守卫不看它，签发流程也不看它，
/// 而写它的那段代码一切正常。
/// </para>
/// <para>
/// ★ <strong>清位要用 <c>&amp;= ~Flag</c>，不要赋 <see cref="None"/>。</strong>
/// 直接赋 <c>None</c> 会把同时欠着的其它事一起抹掉（接受邀请时把「必须改密」清了），
/// 而它既不报错也不会让测试变红。
/// </para>
/// <para>
/// ★ 前端也读这个值。JS 的按位运算安全上限是 2^31，目前最高只用到
/// <c>1 &lt;&lt; 10</c>，远在安全区内 —— 但要加到 <c>1 &lt;&lt; 31</c> 时记得
/// C# 侧不会出错而 JS 侧会。
/// </para>
/// </remarks>
[Flags]
public enum PendingUserActions
{
    /// <summary>
    /// 什么都不欠。<c>default</c>，因此加列迁移之前的存量账号全部落在这里 ——
    /// 这正是要的：既有账号一个都不该因为多了一列而被挡在门外或被要求办事。
    /// </summary>
    None = 0,

    // ────────── 阻断位：守卫直接拒绝，人进不来 ──────────

    /// <summary>
    /// 账号是管理员替别人开好的，本人还没接受邀请。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★★ <strong>这是安全边界，不能用账号锁定代劳。</strong>邀请创建的账号确实会同时被
    /// 置上锁定，但那只是搭便车（让既有的「活跃用户」口径自动排除它）；
    /// 「启用」与「解锁」本来就是清掉 <c>LockoutEnd</c> ——
    /// 管理员对一个未接受邀请的账号点一下「启用」，锁定守卫就如其所愿地放行了，
    /// 而那个账号没有密码、没有二次验证、角色却已按管理员的意思预设好。
    /// </para>
    /// <para>
    /// 挡住的最短路径是验证码登录：它按邮箱找到预建账号、走「用户已存在」分支、
    /// 顺手把 <c>EmailConfirmed</c> 置 true、然后照常签发。
    /// 只要开着 <c>AllowCodeLogin</c>，被邀请人根本不必点那条链接，收一封邮件就能进来。
    /// </para>
    /// </remarks>
    InvitationPending = 1 << 0,

    // 1 << 1 预留给 Staged（已建号但还没发出邀请，用于批量导入后统一发送；Okta 的
    // STAGED / PROVISIONED 之分）。它同样是阻断位，所以留在这一段。

    // ────────── 义务位：放行，但先得办完 ──────────

    /// <summary>
    /// 必须先修改密码才能使用系统。
    /// </summary>
    /// <remarks>
    /// 两个来源：管理员设了临时密码并要求下次登录修改；或者密码按
    /// <c>Identity:PasswordPolicy:PasswordExpirationDays</c> 到期。
    /// ★ 后者此前的行为是**直接拒绝登录**并让人自己去走找回密码流程收邮件 ——
    /// 那既不是本人想要的，也不是「过期」这个词的意思。
    /// </remarks>
    ChangePassword = 1 << 8,

    /// <summary>
    /// 必须先绑定身份验证器（TOTP）。留给消费应用按角色施加的策略，框架自身不设置它。
    /// </summary>
    EnrollTotp = 1 << 9,

    /// <summary>
    /// 必须先确认邮箱。留给消费应用，框架自身不设置它
    /// （框架既有的 <c>Registration.RequireConfirmedEmail</c> 走的是另一条拒绝登录的路）。
    /// </summary>
    ConfirmEmail = 1 << 10,

    // ────────── 分类：两个集合必须覆盖上面每一个位 ──────────

    /// <summary>
    /// 阻断登录的位。<see cref="PendingActionsLoginGuard"/> 读这一组，命中即 403。
    /// </summary>
    Blocking = InvitationPending,

    /// <summary>
    /// 放行但带义务的位。<c>AuthService.IssueTokenAsync</c> 读这一组，命中即发挑战。
    /// </summary>
    Obligations = ChangePassword | EnrollTotp | ConfirmEmail,
}
