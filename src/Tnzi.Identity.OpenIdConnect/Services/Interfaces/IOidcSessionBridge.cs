namespace Tnzi.Identity.OpenIdConnect.Services;

/// <summary>
/// 会话桥：让浏览器在 OIDC 端点上证明「我已登录平台」。
/// </summary>
/// <remarks>
/// <para>
/// 平台本身的登录是 JwtBearer，令牌由前端脚本持有；依赖方把浏览器跳到 <c>/connect/authorize</c> 时
/// 那次顶层导航不带任何 bearer 头，服务端认不出人。会话桥的做法是：已登录的 SPA 带 bearer 调一次
/// <see cref="EstablishAsync"/>，服务端把「哪个用户、哪个平台会话」加密进一枚 cookie；之后每次读它
/// （<see cref="ResolveAsync"/>）都回查平台会话仍有效、账号仍可登录，所以它活不过平台会话。
/// </para>
/// </remarks>
public interface IOidcSessionBridge
{
    /// <summary>
    /// 用当前请求的平台身份（bearer 令牌里的用户与 <c>session_id</c>）签发会话桥 cookie。
    /// </summary>
    /// <returns>令牌不带会话标识（遗留令牌）时 400；会话已失效时 401。</returns>
    Task<Result> EstablishAsync(HttpContext httpContext);

    /// <summary>
    /// 读会话桥 cookie 并回查平台会话。任何一步不成立都返回 null（并清掉那枚失效的 cookie）。
    /// </summary>
    Task<OidcBrowserSession?> ResolveAsync(HttpContext httpContext);

    /// <summary>清掉会话桥 cookie。</summary>
    void Clear(HttpContext httpContext);
}

/// <summary>
/// 经会话桥确认的浏览器会话。
/// </summary>
/// <param name="User">已确认仍可登录的用户。</param>
/// <param name="SessionId">背后的平台会话。</param>
public sealed record OidcBrowserSession(User User, Guid SessionId);
