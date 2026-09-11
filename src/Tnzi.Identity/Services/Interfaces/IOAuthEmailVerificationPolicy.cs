namespace Tnzi.Identity.Services;

/// <summary>
/// 回答一个问题：这次第三方回调带来的邮箱，提供商<b>证实过</b>吗。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <strong>这个判定是「按邮箱自动认领账号」能不能成立的全部前提。</strong>
/// <c>HandleOAuthCallbackAsync</c> 在找不到 <c>(provider, providerKey)</c> 记录时，会拿
/// 提供商给的邮箱去查本地账号，查到就关联并登录。若那个邮箱只是用户在第三方那边
/// <b>自己填的</b>，这一步就等于：谁都能用受害者的邮箱注册一个第三方账号，然后凭它登进
/// 受害者的本地账号 —— 不需要密码，也不需要两步验证。
/// </para>
/// <para>
/// ★ 做成可替换的契约而不是写死一张表：各家提供商的断言方式差别很大，且会变
/// （GitHub 要另调 <c>/user/emails</c> 才拿得到 <c>verified</c>；企业目录里的地址由域所有者背书）。
/// 消费应用把自己接的提供商摸清楚之后，注册自己的实现替换默认判定
/// （<see cref="DefaultOAuthEmailVerificationPolicy"/> 走 <c>TryAdd</c>，注册自己的即可覆盖）。
/// </para>
/// <para>
/// ★ <strong>默认实现宁可答「不知道」也不答「已验证」。</strong>答错的两个方向代价不对称：
/// 误判为未验证 = 用户多走一次「先正常登录、再从个人中心绑定」；
/// 误判为已验证 = 账号可被接管。
/// </para>
/// </remarks>
public interface IOAuthEmailVerificationPolicy
{
    /// <summary>
    /// 这次回调的邮箱是否可以直接用来认领 / 建立账号。
    /// </summary>
    /// <param name="provider">提供商键（小写，如 <c>google</c>）。</param>
    /// <param name="principal">第三方回调解析出的主体，含全部 claim。</param>
    /// <param name="email">从 claim 里提取出的邮箱（可能为空）。</param>
    bool IsEmailVerified(string provider, ClaimsPrincipal principal, string? email);
}
