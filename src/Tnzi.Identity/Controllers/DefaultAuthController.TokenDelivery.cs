namespace Tnzi.Identity.Controllers;

/// <summary>
/// <see cref="DefaultAuthController"/> 的令牌交付部分：cookie 模式下与刷新令牌 cookie 打交道的
/// 那几件<b>过滤器管不到</b>的事。
/// </summary>
/// <remarks>
/// <para>
/// JSON 响应体里的刷新令牌由全局的 <see cref="Mvc.RefreshTokenDeliveryFilter"/> 统一搬进 cookie ——
/// 判据是响应载荷的形状，与谁产生了这个响应无关，因此消费方覆写签发端点也照样受管辖。
/// 这里只剩三件它够不着的：
/// </para>
/// <list type="number">
/// <item><b>读回来</b>：刷新端点要从 cookie 取令牌（请求侧，不是响应侧）。</item>
/// <item><b>清掉</b>：登出要删这枚 cookie。</item>
/// <item><b>OAuth 回调</b>：它不返回 JSON，而是生成一张 HTML 用 <c>postMessage</c> 交给打开它的窗口，
/// 载荷压根不经过 MVC 的结果管线。</item>
/// </list>
/// </remarks>
public partial class DefaultAuthController
{
    private static readonly TokenDeliveryOptions DefaultTokenDelivery = new();

    /// <summary>
    /// 令牌交付配置。<c>IdentityOptions</c> 是可选依赖（构造参数可为 null），
    /// 取不到时按默认值处理 —— 即 <see cref="TokenDeliveryMode.Bearer"/>，与升级前逐字相同。
    /// </summary>
    protected TokenDeliveryOptions TokenDelivery
        => IdentityOptions?.CurrentValue?.TokenDelivery ?? DefaultTokenDelivery;

    /// <summary>当前部署是否走 cookie 交付。</summary>
    protected bool UseCookieTokenDelivery => TokenDelivery.Mode == TokenDeliveryMode.Cookie;

    /// <summary>
    /// 取本次请求携带的刷新令牌：cookie 模式下优先读 cookie，读不到再退回请求体。
    /// </summary>
    /// <remarks>
    /// 退回请求体不是多余的：切换到 cookie 模式的那一刻，正在线上的客户端手里
    /// 还攥着上一形态的令牌。不接受请求体，切换即等于把所有人登出一次。
    /// </remarks>
    protected string? ResolveRefreshToken(string? fromBody)
    {
        if (UseCookieTokenDelivery
            && Request.Cookies.TryGetValue(TokenDelivery.CookieName, out var fromCookie)
            && !string.IsNullOrEmpty(fromCookie))
        {
            return fromCookie;
        }

        return fromBody;
    }

    /// <summary>清除刷新令牌 cookie（登出时调用）。</summary>
    protected void ClearRefreshTokenCookie()
    {
        if (!UseCookieTokenDelivery)
        {
            return;
        }

        RefreshTokenCookie.Clear(HttpContext, TokenDelivery);
    }

    /// <summary>
    /// 交付一次 OAuth 回调结果：cookie 模式下把刷新令牌写进 cookie，并从要发给页面的载荷里抹掉。
    /// </summary>
    /// <remarks>
    /// ★ <b>这条路径极易漏掉，而漏掉就等于这个模式没有意义。</b>OAuth 回调不返回 JSON，
    /// 它生成一张 HTML、用 <c>postMessage</c> 把结果交给打开它的窗口 —— 载荷不经过 MVC 的结果管线，
    /// 全局过滤器看不到它。一条没被收口的签发路径，足以让「浏览器里没有可读凭据」这个前提整体失效。
    /// </remarks>
    protected OAuthCallbackResultDto DeliverOAuthTokens(OAuthCallbackResultDto result)
    {
        Check.NotNull(result);

        if (!UseCookieTokenDelivery || !result.Success || string.IsNullOrEmpty(result.RefreshToken))
        {
            return result;
        }

        var refreshDays = IdentityOptions?.CurrentValue?.Jwt?.RefreshTokenExpirationDays ?? 7;
        RefreshTokenCookie.Write(HttpContext, TokenDelivery, result.RefreshToken, refreshDays * 24 * 60 * 60);

        // 换一个对象而不是就地清空：调用方或测试可能持有这个实例。
        return new OAuthCallbackResultDto
        {
            Success = result.Success,
            AccessToken = result.AccessToken,
            RefreshToken = null,
            ExpiresAt = result.ExpiresAt,
            RequiresRegistration = result.RequiresRegistration,
            UserInfo = result.UserInfo,
            ErrorMessage = result.ErrorMessage,
        };
    }
}
