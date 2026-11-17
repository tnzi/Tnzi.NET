namespace Tnzi.Identity.OpenIdConnect.Metadata;

/// <summary>
/// OpenID Connect 提供方的路由、cookie 用途、请求头与权限码常量。
/// </summary>
public static class OidcConstants
{
    /// <summary>协议端点的路由前缀（不带 <c>api/</c>：依赖方与代理按标准路径访问它们）。</summary>
    public const string RoutePrefix = "connect";

    /// <summary>授权端点。</summary>
    public const string AuthorizePath = "connect/authorize";

    /// <summary>令牌端点。</summary>
    public const string TokenPath = "connect/token";

    /// <summary>用户信息端点。</summary>
    public const string UserInfoPath = "connect/userinfo";

    /// <summary>依赖方发起登出端点。</summary>
    public const string EndSessionPath = "connect/endsession";

    /// <summary>令牌撤销端点（RFC 7009）。</summary>
    public const string RevocationPath = "connect/revoke";

    /// <summary>JWKS 端点。</summary>
    public const string JwksPath = "connect/jwks";

    /// <summary>
    /// 用户在同意页拒绝时，SPA 跳回 authorize 附加的参数名（值为 <see cref="ConsentDeniedValue"/>）。
    /// </summary>
    /// <remarks>
    /// 任何人都能在 URL 上加它，但它只能让一次授权<b>失败</b>，换不来任何东西，故不需要签名。
    /// </remarks>
    public const string ConsentDecisionParameter = "tnzi_consent";

    /// <summary>见 <see cref="ConsentDecisionParameter"/>。</summary>
    public const string ConsentDeniedValue = "denied";

    /// <summary>会话桥 cookie 的 Data Protection 用途串。改了它等于让所有已签发的 cookie 失效。</summary>
    public const string SessionBridgeProtectorPurpose = "Tnzi.Identity.OpenIdConnect.SessionBridge.v1";

    /// <summary>forward_auth 校验通过时回带的请求头：用户 Id。</summary>
    public const string ForwardAuthUserIdHeader = "X-Auth-User-Id";

    /// <summary>forward_auth 校验通过时回带的请求头：显示名。</summary>
    public const string ForwardAuthUserNameHeader = "X-Auth-User-Name";

    /// <summary>forward_auth 校验通过时回带的请求头：邮箱（未验证时不带）。</summary>
    public const string ForwardAuthUserEmailHeader = "X-Auth-User-Email";

    /// <summary>客户端管理的权限码前缀。</summary>
    public const string ClientPermissionPrefix = "identity.oidcClient";

    /// <summary>用户授权（grant）管理的权限码前缀。</summary>
    public const string GrantPermissionPrefix = "identity.oidcGrant";

    /// <summary>客户端：查看。</summary>
    public const string ClientView = ClientPermissionPrefix + ".view";

    /// <summary>客户端：创建。</summary>
    public const string ClientCreate = ClientPermissionPrefix + ".create";

    /// <summary>客户端：修改（含启停与轮换密钥）。</summary>
    public const string ClientUpdate = ClientPermissionPrefix + ".update";

    /// <summary>客户端：删除。</summary>
    public const string ClientDelete = ClientPermissionPrefix + ".delete";

    /// <summary>授权：查看。</summary>
    public const string GrantView = GrantPermissionPrefix + ".view";

    /// <summary>授权：撤销。</summary>
    public const string GrantDelete = GrantPermissionPrefix + ".delete";

    /// <summary>
    /// 本模块总是支持的作用域（<c>openid</c> / <c>profile</c> / <c>email</c> / <c>offline_access</c>）。
    /// </summary>
    public static readonly IReadOnlyList<string> StandardScopes =
    [
        OpenIddictConstants.Scopes.OpenId,
        OpenIddictConstants.Scopes.Profile,
        OpenIddictConstants.Scopes.Email,
        OpenIddictConstants.Scopes.OfflineAccess,
    ];
}

/// <summary>客户端类型。</summary>
public enum OidcClientType
{
    /// <summary>机密客户端：有服务端、能保管密钥（令牌端点要验 client_secret）。</summary>
    Confidential = 0,

    /// <summary>公开客户端：纯前端 / 原生应用，没有密钥，<b>必须</b>用 PKCE。</summary>
    Public = 1,
}

/// <summary>用户同意方式。</summary>
public enum OidcConsentType
{
    /// <summary>不弹同意页（第一方应用：用户与平台之间不存在「要不要把信息交给它」的问题）。</summary>
    Implicit = 0,

    /// <summary>第一次授权时要用户在同意页确认，同意后记为永久授权，直到用户或管理员撤销。</summary>
    Explicit = 1,
}
