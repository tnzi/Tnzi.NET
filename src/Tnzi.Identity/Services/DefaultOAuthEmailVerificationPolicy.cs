namespace Tnzi.Identity.Services;

/// <inheritdoc cref="IOAuthEmailVerificationPolicy"/>
/// <remarks>
/// 按提供商给出保守答案：
/// <list type="bullet">
/// <item><c>google</c> —— 读 <c>email_verified</c> claim。★ 该 claim <b>需要显式映射</b>
/// （见 <c>OAuthExtensions</c> 里的 <c>ClaimActions.MapJsonKey</c>）；没映射时这里读不到，
/// 于是答「未验证」—— 少放行，不多放行。</item>
/// <item><c>microsoft</c>：<b>未验证</b>。框架注册的处理器走 <c>/common</c>（个人账号 + 任意租户），
/// 邮箱取自 Graph <c>/me</c> 的 <c>mail</c> 属性；那是租户管理员可以随手填写的目录数据，
/// 任何一个 Entra 租户的管理员都能把自己租户里某个用户的 <c>mail</c> 设成别人域名下的地址，
/// 而不需要证明拥有那个域名。按「已验证」处理就是让人用别人的邮箱登录别人的账号。
/// 只接受单一已知租户、或改用携带 <c>xms_edov</c> 的 OIDC id_token 的部署，可以实现本契约自行放开。</item>
/// <item><c>github</c> —— 资料邮箱<b>可以是未验证的</b>，且 <c>verified</c> 只出现在
/// <c>/user/emails</c> 这个额外请求里，默认实现不发那个请求，故判为未验证。
/// 要让 GitHub 支持自动关联，就实现本契约并把那次请求补上。</item>
/// <item><c>facebook</c> / <c>twitter</c> —— 没有可靠的验证断言，判为未验证。</item>
/// <item>其它 —— 未验证。<b>新提供商默认落在安全的一侧</b>，这是 <c>switch</c> 用
/// <c>_ =&gt; false</c> 收尾而不是逐个列举「不安全的那几家」的理由。</item>
/// </list>
/// </remarks>
public class DefaultOAuthEmailVerificationPolicy : IOAuthEmailVerificationPolicy
{
    /// <inheritdoc />
    public virtual bool IsEmailVerified(string provider, ClaimsPrincipal principal, string? email)
    {
        Check.NotNull(principal);

        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        return provider?.ToLowerInvariant() switch
        {
            "google" => ReadBooleanClaim(principal, "email_verified"),
            _ => false,
        };
    }

    /// <summary>读一个布尔 claim；缺失或不可解析一律按 false（「不知道」不等于「已验证」）。</summary>
    private static bool ReadBooleanClaim(ClaimsPrincipal principal, string claimType)
    {
        var raw = principal.FindFirstValue(claimType);
        return !string.IsNullOrEmpty(raw) && bool.TryParse(raw, out var value) && value;
    }
}
