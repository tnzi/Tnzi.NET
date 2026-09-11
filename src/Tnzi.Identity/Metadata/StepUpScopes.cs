namespace Tnzi.Identity.Metadata;

/// <summary>
/// 框架自带端点使用的二次确认范围名。
/// </summary>
/// <remarks>
/// <para>
/// 范围名是自由字符串（<c>[RequireStepUp("...")]</c>），消费应用可以自己定义任意范围。
/// 这里只收拢框架自己那几个端点用到的常量，避免同一个范围在两处被拼成两个不同的字符串
/// —— 那不会报错，只会让「刚为改邮箱做过确认」在删账号那里不算数，或者反过来。
/// </para>
/// <para>
/// ★ <strong>按动作分组，不共用一个范围。</strong>一次确认只覆盖同名范围，
/// 这正是它的价值：为「换绑邮箱」做的生物识别，不该顺便把「删除账户」也放行。
/// 分得越细越安全，但也越啰嗦；这里的粒度是「后果同量级的动作归一组」。
/// </para>
/// <para>
/// ★ 未启用 <c>Identity:StepUp</c> 时这些标注不拦任何请求（见
/// <see cref="Tnzi.Identity.Mvc.RequireStepUpAttribute"/>），所以给端点加上它们对既有部署
/// 是零影响的增量改动 —— 开关一开才生效。
/// </para>
/// </remarks>
public static class StepUpScopes
{
    /// <summary>
    /// 拆除两步验证：关总开关、暂停、禁用 TOTP、禁用单一方式。
    /// </summary>
    /// <remarks>
    /// ★ 这一组是最该有二次确认的：拿到一枚被盗访问令牌的人，第一件事就是把第二因子摘掉，
    /// 而摘掉之后受害者连「重新登录时会被要求验证」这条兜底都没有了。
    /// </remarks>
    public const string TwoFactorManage = "identity.twofactor.manage";

    /// <summary>停用或删除本人账户。不可逆或近乎不可逆，且没有第二个人会复核。</summary>
    public const string AccountDestroy = "identity.account.destroy";

    /// <summary>
    /// 换绑邮箱 / 手机号。
    /// </summary>
    /// <remarks>
    /// ★ 换绑流程本身已经验证了<b>新地址</b>（验证码发到新地址），但那证明的是
    /// 「这个地址是真的」，不是「提出更换的人是账号主人」。找回密码与部分 2FA 都挂在这两个字段上，
    /// 所以换掉它们等于换掉账号的恢复路径 —— 这一步需要本人在场。
    /// </remarks>
    public const string ContactChange = "identity.contact.change";
}
