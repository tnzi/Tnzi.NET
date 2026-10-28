namespace Tnzi.Identity.Tests;

/// <summary>
/// 默认的「提供商给的邮箱算不算已证实」判定。答 true 的那一家，回调就会按邮箱把外部身份挂到本地已有账号上并签发令牌。
/// </summary>
public class DefaultOAuthEmailVerificationPolicyTests
{
    private readonly DefaultOAuthEmailVerificationPolicy _policy = new();

    private static ClaimsPrincipal PrincipalWith(string email, params Claim[] extra)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "key"), new(ClaimTypes.Email, email) };
        claims.AddRange(extra);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    /// <summary>
    /// ★★★ Microsoft 的邮箱不能按「已证实」处理。
    /// </summary>
    /// <remarks>
    /// 框架注册的 Microsoft 账号处理器走 <c>/common</c>（个人账号 + 任意租户），邮箱取自 Graph <c>/me</c> 的
    /// <c>mail</c> 属性：任何一个 Entra 租户的管理员都能把自己租户里某个用户的 <c>mail</c> 设成别人域名下的地址，
    /// 不需要证明拥有那个域名。判成已证实，就是用别人的邮箱登录别人的账号。
    /// </remarks>
    [Theory]
    [InlineData("microsoft")]
    [InlineData("Microsoft")]
    public void Microsoft_IsNotTreatedAsVerified(string provider)
        => Assert.False(_policy.IsEmailVerified(provider, PrincipalWith("victim@company.example"), "victim@company.example"));

    [Theory]
    [InlineData("github")]
    [InlineData("facebook")]
    [InlineData("twitter")]
    [InlineData("some-new-provider")]
    public void ProvidersWithoutAReliableAssertion_AreNotVerified(string provider)
        => Assert.False(_policy.IsEmailVerified(provider, PrincipalWith("someone@example.com"), "someone@example.com"));

    /// <summary>对照：Google 按它自己签的 <c>email_verified</c> 判定，这条路没有被一并关掉。</summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Google_FollowsItsEmailVerifiedClaim(string claimValue, bool expected)
        => Assert.Equal(
            expected,
            _policy.IsEmailVerified("google", PrincipalWith("someone@example.com", new Claim("email_verified", claimValue)), "someone@example.com"));
}
