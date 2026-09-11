namespace Tnzi.Identity.Metadata;

/// <summary>
/// 一次性验证码的用途。**发码时写死、验码时精确匹配**，一个用途发出的码不能拿去完成另一个用途。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 这个枚举存在的唯一理由是<strong>阻断跨流程重放</strong>。此前所有流程共用一张
/// <see cref="Entities.TwoFactorCode"/> 表、只按 <c>(Address, Code, Type)</c> 匹配，于是
/// 「为登录发的码」可以直接拿去重置密码、确认换绑、或完成一次敏感操作的二次确认 ——
/// 这些流程的后果轻重完全不同，却共享同一个码池。
/// </para>
/// <para>
/// 典型可利用形态是钓鱼放大：让人以为自己在做 A（「验证一下你的邮箱」）而把码念出来，
/// 攻击者拿它去做 B（重置密码）。用户看到的短信文案属于 A，实际效力却是 B 的。
/// </para>
/// <para>
/// ★ <see cref="Unknown"/> 是 <c>default</c>，**永不匹配任何验证请求**。加列迁移之前发出、
/// 尚未使用的码会落在这个值上并因此失效 —— 代价是那几分钟内的在途码需要重发一次，
/// 换来的是不留「万能用途」这个后门。宁可让少数人重发，也不要一个能绕过全部用途绑定的值。
/// </para>
/// </remarks>
public enum VerificationCodePurpose
{
    /// <summary>
    /// 未指定。仅出现在加列迁移之前写入的历史行上，**永不匹配**任何验证请求。
    /// 新代码路径一律显式给出用途，不要使用这个值发码。
    /// </summary>
    Unknown = 0,

    /// <summary>登录时的两步验证挑战（密码 / passkey 校验通过后的第二个因子）。</summary>
    TwoFactor = 1,

    /// <summary>免密的验证码登录（<c>POST /auth/code-login</c>）。</summary>
    CodeLogin = 2,

    /// <summary>验证码找回密码（<c>POST /auth/password-recovery/reset</c>）。</summary>
    PasswordRecovery = 3,

    /// <summary>快速注册（<c>POST /auth/quick-register</c>）。</summary>
    Registration = 4,

    /// <summary>
    /// 换绑联系方式（换邮箱 / 换手机号）。码发往<strong>新地址</strong>，
    /// 证明的是「申请人能收到这个新地址的信」。
    /// </summary>
    ChangeContact = 5,

    /// <summary>
    /// 敏感操作的二次确认（step-up）。★ 与 <see cref="TwoFactor"/> 刻意分开：
    /// 一个为登录发出的码不应该能确认一笔转账，反之亦然。
    /// </summary>
    StepUp = 6,

    /// <summary>
    /// 登录时被要求「先确认邮箱」这件待办（<see cref="PendingUserActions.ConfirmEmail"/>）。
    /// </summary>
    /// <remarks>
    /// ★ <strong>刻意不复用 <see cref="ChangeContact"/>。</strong>两者都往邮箱发码，
    /// 但问的是不同的问题：换绑问「你能收到<b>这个新地址</b>吗」，本用途问
    /// 「你还能收到<b>账号上现有的这个地址</b>吗」。共用一个用途就是把两个流程放进同一个码池 ——
    /// 而那正是这个枚举存在要防的事：让人以为自己在做 A 而把码念出来，攻击者拿它去做 B。
    /// 撞击窗口窄（新地址通常不等于现地址）不是共用的理由，窄不等于没有。
    /// </remarks>
    ConfirmEmail = 7,
}
