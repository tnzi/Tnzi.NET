namespace Tnzi.Identity.Mvc;

/// <summary>
/// 携带刷新令牌的响应载荷。实现它，本类型就自动受
/// <see cref="RefreshTokenDeliveryFilter"/> 管辖。
/// </summary>
/// <remarks>
/// <para>
/// 做成标记接口而不是「在每个签发端点上记得调一次」，是因为后者已经漏过一次：
/// 靠约定要求每个签发端点多调一步，漏一条就让「浏览器里没有可读凭据」这个前提整体失效。
/// </para>
/// <para>
/// ⚠ <b>本过滤器只覆盖 JSON 响应</b>（<c>ObjectResult</c>，见
/// <see cref="RefreshTokenDeliveryFilter.OnResultExecuting"/> 的第一道判断）。
/// OAuth 回调不在其内：它返回一张 HTML（<c>ContentResult</c>），用 <c>postMessage</c>
/// 把结果交给打开它的窗口，因而由 <c>DefaultAuthController.DeliverOAuthTokens</c>
/// 显式处理。<b>新增一条不返回 JSON 的签发路径时，这里接不住它</b> —— 那条路径必须
/// 自己交付，就像 OAuth 回调那样。
/// </para>
/// <para>
/// ★ 更要紧的是<b>消费方覆写</b>：<c>[DefaultController]</c> 的端点可以被应用整组替换，
/// 替换者写 <c>return result.ToApiResult()</c> 是最自然的写法 —— 靠约定要求每个人记得
/// 多调一步，是把一个安全前提押在纪律上。过滤器管的是「响应里有没有这个字段」，
/// 与谁产生了这个响应无关。
/// </para>
/// </remarks>
public interface IRefreshTokenCarrier
{
    /// <summary>本次响应携带的刷新令牌；没有则返回 null 或空串。</summary>
    string? ReadRefreshToken();

    /// <summary>刷新令牌的剩余寿命（秒），用于设置 cookie 过期；未知返回 null。</summary>
    int? ReadRefreshTokenLifetimeSeconds();

    /// <summary>把刷新令牌从响应载荷里抹掉（已改为经 cookie 交付）。</summary>
    void ClearRefreshToken();
}

/// <summary>
/// Cookie 交付模式下，把响应体里的刷新令牌搬进 <c>HttpOnly</c> cookie。
/// </summary>
/// <remarks>
/// <para>
/// 全局结果过滤器，仅在 <c>Identity:TokenDelivery:Mode = Cookie</c> 时做事；
/// Bearer 模式（默认）下第一行就返回，行为与升级前逐字相同。
/// </para>
/// <para>
/// 判据是<b>响应载荷的形状</b>（实现了 <see cref="IRefreshTokenCarrier"/>），
/// 不是端点名单 —— 名单要有人维护，而漏一条的代价是这个模式在那条路径上静默失效。
/// </para>
/// </remarks>
public sealed class RefreshTokenDeliveryFilter : IResultFilter
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> DataProperties = new();

    private readonly IOptionsMonitor<IdentityOptions> _options;

    public RefreshTokenDeliveryFilter(IOptionsMonitor<IdentityOptions> options)
    {
        _options = Check.NotNull(options);
    }

    /// <inheritdoc />
    public void OnResultExecuting(ResultExecutingContext context)
    {
        Check.NotNull(context);

        var delivery = _options.CurrentValue.TokenDelivery;
        if (delivery.Mode != TokenDeliveryMode.Cookie)
        {
            return;
        }

        if (context.Result is not ObjectResult { Value: { } value })
        {
            return;
        }

        var carrier = ResolveCarrier(value);
        var refreshToken = carrier?.ReadRefreshToken();
        if (carrier == null || string.IsNullOrEmpty(refreshToken))
        {
            return;
        }

        RefreshTokenCookie.Write(context.HttpContext, delivery, refreshToken, carrier.ReadRefreshTokenLifetimeSeconds());
        carrier.ClearRefreshToken();
    }

    /// <inheritdoc />
    public void OnResultExecuted(ResultExecutedContext context)
    {
        // 交付发生在写响应之前，这里无事可做。
    }

    /// <summary>
    /// 从响应值里取出携带者：可能是载荷本身，也可能包在某个结果信封的 <c>Data</c> 里。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用一次（按类型缓存的）反射而不是逐个 <c>ApiResult&lt;具体类型&gt;</c> 模式匹配：
    /// 后者要为每一个新的信封类型补一行，而「忘了补那一行」正是本过滤器要消灭的失败形态。
    /// </para>
    /// <para>
    /// ★ <b>刻意不要求信封是 <c>IApiResult</c>。</b>第一版加了这道判据，结果
    /// <c>ApiResult&lt;T&gt;.Success(...)</c> 实际返回的是继承来的 <c>Result&lt;T&gt;</c>（不实现该接口），
    /// 于是过滤器一声不吭地跳过 —— 又一个「按类型名单判断」的翻车。
    /// 判据只该有一条：这个响应里有没有刷新令牌。
    /// </para>
    /// </remarks>
    private static IRefreshTokenCarrier? ResolveCarrier(object value)
    {
        if (value is IRefreshTokenCarrier direct)
        {
            return direct;
        }

        var dataProperty = DataProperties.GetOrAdd(value.GetType(), static type => FindDataProperty(type));

        return dataProperty?.GetValue(value) as IRefreshTokenCarrier;
    }

    /// <summary>
    /// 找到<b>最派生的那一个</b> <c>Data</c> 声明。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 结果类型的继承链上 <c>Data</c> 被 <c>new</c> 隐藏过一次
    /// （<c>BaseResult&lt;object&gt;.Data</c> ← <c>Result&lt;T&gt;.Data</c>），两者是<b>各自独立的后备字段</b>：
    /// 取错那一个会拿到恒为 null 的值，过滤器于是一声不吭地什么都不做。
    /// </para>
    /// <para>
    /// 实测 <c>GetProperty("Data")</c> 在当前这条继承链上确实会返回最派生的那一个（变异验证过，
    /// 换成它这一组测试仍全绿），所以这个遍历<b>不是</b>在修复一个现存缺陷。留着它是因为
    /// 「隐藏成员时反射返回哪一个」依赖运行时的 hide-by-name 解析，同名不同类型还可能抛
    /// <c>AmbiguousMatchException</c>；显式取最派生的那一个是确定的，且把这条依赖写在了明面上。
    /// </para>
    /// </remarks>
    private static PropertyInfo? FindDataProperty(Type type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var property = current.GetProperty(
                "Data", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (property != null)
            {
                return property;
            }
        }

        return null;
    }
}

/// <summary>
/// 刷新令牌 cookie 的写入与清除。控制器（OAuth 回调、登出）与结果过滤器共用同一份口径。
/// </summary>
public static class RefreshTokenCookie
{
    /// <summary>写入刷新令牌 cookie。</summary>
    public static void Write(HttpContext httpContext, TokenDeliveryOptions delivery, string refreshToken, int? lifetimeSeconds)
    {
        Check.NotNull(httpContext);
        Check.NotNull(delivery);
        Check.NotNullOrWhiteSpace(refreshToken);

        httpContext.Response.Cookies.Append(
            delivery.CookieName, refreshToken, BuildOptions(httpContext, delivery, lifetimeSeconds));
    }

    /// <summary>清除刷新令牌 cookie。</summary>
    public static void Clear(HttpContext httpContext, TokenDeliveryOptions delivery)
    {
        Check.NotNull(httpContext);
        Check.NotNull(delivery);

        httpContext.Response.Cookies.Delete(
            delivery.CookieName, BuildOptions(httpContext, delivery, lifetimeSeconds: null));
    }

    private static CookieOptions BuildOptions(HttpContext httpContext, TokenDeliveryOptions delivery, int? lifetimeSeconds)
        => new()
        {
            HttpOnly = true,
            // ★ 跟随传输而不是恒 true：恒 true 会让 http 的本地开发环境根本存不下这枚 cookie，
            // 开发者的第一反应是把整个 cookie 模式关掉 —— 一个在开发环境里用不了的安全模式等于没有。
            // 生产是 HTTPS，这里自然就是 true。
            Secure = httpContext.Request.IsHttps,
            SameSite = delivery.SameSite,
            Path = string.IsNullOrWhiteSpace(delivery.CookiePath) ? "/" : delivery.CookiePath,
            Domain = string.IsNullOrWhiteSpace(delivery.CookieDomain) ? null : delivery.CookieDomain,
            // 会话 cookie（不设 Expires）少一份落盘副本，但关掉浏览器就掉线；
            // 刷新令牌的意义正是「关掉浏览器再打开还在」，故按令牌自身寿命设过期。
            Expires = lifetimeSeconds.HasValue ? DateTimeOffset.UtcNow.AddSeconds(lifetimeSeconds.Value) : null,
        };
}
