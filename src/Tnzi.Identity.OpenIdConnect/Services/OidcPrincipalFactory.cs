using System.Collections.Immutable;
using Microsoft.IdentityModel.Tokens;

namespace Tnzi.Identity.OpenIdConnect.Services;

/// <summary>
/// 把平台用户翻译成 OIDC 声明：签发令牌时的 <see cref="ClaimsPrincipal"/> 与 userinfo 的响应体出自同一份取值，
/// 两边不会对同一个人说出两种话。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>sub</c>：用户 Id（恒有，进 ID token 与访问令牌）。</item>
/// <item><c>profile</c> 作用域：<c>name</c>（用户详情的全名，没有时用用户名）、<c>preferred_username</c>、
/// <c>given_name</c> / <c>family_name</c>（有才给）。</item>
/// <item><c>email</c> 作用域：<c>email</c> 与 <c>email_verified</c>（有邮箱才给）。</item>
/// </list>
/// 每次签发（含刷新）都从库里重新取值，改名、改邮箱在下一次刷新后反映到依赖方。
/// </remarks>
public class OidcPrincipalFactory
{
    private readonly IRepository<UserDetail, Guid>? _userDetails;

    /// <summary>创建工厂。</summary>
    public OidcPrincipalFactory(IRepository<UserDetail, Guid>? userDetails = null)
    {
        _userDetails = userDetails;
    }

    /// <summary>为签发令牌构造身份：声明、作用域与每条声明的去向（ID token / 访问令牌）。</summary>
    public virtual async Task<ClaimsPrincipal> CreatePrincipalAsync(User user, ImmutableArray<string> scopes)
    {
        Check.NotNull(user);

        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        foreach (var (type, value) in await GetClaimsAsync(user, scopes))
        {
            // email_verified 在令牌里必须是 JSON 布尔值，不是字符串 "true"。
            identity.AddClaim(type == Claims.EmailVerified
                ? new Claim(type, value, ClaimValueTypes.Boolean)
                : new Claim(type, value));
        }

        identity.SetScopes(scopes);
        identity.SetDestinations(static claim => claim.Type switch
        {
            // 主体在两种令牌里都要有：依赖方凭 ID token 认人，资源方凭访问令牌认人。
            Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.Name or Claims.PreferredUsername or Claims.GivenName or Claims.FamilyName
                when claim.Subject?.HasScope(Scopes.Profile) == true => [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.Email or Claims.EmailVerified
                when claim.Subject?.HasScope(Scopes.Email) == true => [Destinations.AccessToken, Destinations.IdentityToken],
            _ => [Destinations.AccessToken],
        });

        return new ClaimsPrincipal(identity);
    }

    /// <summary>userinfo 端点的响应体（按作用域裁剪；<c>email_verified</c> 是布尔值）。</summary>
    public virtual async Task<Dictionary<string, object>> CreateUserInfoAsync(User user, ImmutableArray<string> scopes)
    {
        Check.NotNull(user);

        var info = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (type, value) in await GetClaimsAsync(user, scopes))
        {
            info[type] = type == Claims.EmailVerified ? bool.Parse(value) : value;
        }

        return info;
    }

    /// <summary>按作用域取出的声明（类型 → 字符串值）。</summary>
    protected virtual async Task<IReadOnlyList<(string Type, string Value)>> GetClaimsAsync(User user, ImmutableArray<string> scopes)
    {
        var claims = new List<(string, string)> { (Claims.Subject, user.Id.ToString()) };

        if (scopes.Contains(Scopes.Profile))
        {
            var detail = _userDetails == null
                ? null
                : await _userDetails.AsQueryable().AsNoTracking().FirstOrDefaultAsync(d => d.UserId == user.Id);

            var fullName = detail?.FullName;
            claims.Add((Claims.Name, string.IsNullOrWhiteSpace(fullName) ? user.UserName ?? user.Id.ToString() : fullName));
            if (!string.IsNullOrWhiteSpace(user.UserName))
            {
                claims.Add((Claims.PreferredUsername, user.UserName));
            }

            if (!string.IsNullOrWhiteSpace(detail?.FirstName))
            {
                claims.Add((Claims.GivenName, detail.FirstName));
            }

            if (!string.IsNullOrWhiteSpace(detail?.LastName))
            {
                claims.Add((Claims.FamilyName, detail.LastName));
            }
        }

        if (scopes.Contains(Scopes.Email) && !string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add((Claims.Email, user.Email));
            claims.Add((Claims.EmailVerified, user.EmailConfirmed ? "true" : "false"));
        }

        return claims;
    }
}
