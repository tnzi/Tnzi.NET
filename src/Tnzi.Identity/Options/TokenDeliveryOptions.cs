namespace Tnzi.Identity.Options;

/// <summary>
/// 令牌交付方式：刷新令牌用什么形态交给浏览器。
/// </summary>
public enum TokenDeliveryMode
{
    /// <summary>
    /// 默认：刷新令牌随响应体一起返回，由前端自行保存（框架前端默认存 <c>localStorage</c>）。
    /// </summary>
    Bearer = 0,

    /// <summary>
    /// 刷新令牌改为 <c>HttpOnly</c> cookie，响应体里不再出现；access token 仍在响应体里，
    /// 由前端只存在内存。
    /// </summary>
    Cookie = 1,
}

/// <summary>
/// 令牌交付配置。配置路径 <c>Identity:TokenDelivery</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>要解决的是「凭据放在浏览器里能被读走」这件事。</b>信息窃取类恶意软件读的是浏览器的
/// 本地文件（cookie 库、localStorage、IndexedDB），拿到刷新令牌后在自己的机器上直接续期，
/// 不需要密码，也绕过两步验证 —— 因为两步验证发生在登录那一刻，而令牌是登录之后的产物。
/// </para>
/// <para>
/// ★ <b>换成 HttpOnly cookie 挡不住本机恶意软件</b>（cookie 库同样是本地文件），
/// 但它换来两件别的东西：
/// <list type="number">
/// <item>XSS 读不到它。<c>localStorage</c> 里的令牌，页面上任何一段脚本都能取走；
/// HttpOnly cookie 连同源脚本也读不到。RFC 10017（OAuth 2.0 for Browser-Based Applications）
/// 因此只认两种形态：令牌只存内存，或者由后端持有、浏览器只拿 cookie。</item>
/// <item><b>这是接入设备绑定的前提。</b>浏览器侧的设备绑定（DBSC，Chrome 2026 年起在
/// Windows/macOS 上正式提供）把会话绑到 TPM 里不可导出的私钥上，使被盗 cookie 在别的设备上
/// 无法续期 —— 而它<b>只保护 cookie</b>，对 <c>localStorage</c> 里的 bearer 令牌无能为力。
/// 只要交付形态是 bearer，这条路就是关着的。</item>
/// </list>
/// </para>
/// <para>
/// ★ <b>默认仍是 <see cref="TokenDeliveryMode.Bearer"/></b>：改交付形态会改变响应体形状，
/// 需要前端配合（`@tnzi/core` 的 <c>createTnziClient({ tokenDelivery: 'cookie' })</c>）。
/// 让它默认开启，会让所有既有消费方在升级当天集体掉线。
/// </para>
/// </remarks>
public class TokenDeliveryOptions
{
    /// <summary>
    /// 交付方式。默认 <see cref="TokenDeliveryMode.Bearer"/>（保持既有行为）。
    /// </summary>
    public TokenDeliveryMode Mode { get; set; } = TokenDeliveryMode.Bearer;

    /// <summary>
    /// 刷新令牌 cookie 的名称。默认 <c>tnzi_rt</c>。
    /// </summary>
    /// <remarks>
    /// 同一个域名下跑多个本框架应用时必须各取一个，否则后登录的会覆盖先登录的那一份。
    /// </remarks>
    public string CookieName { get; set; } = "tnzi_rt";

    /// <summary>
    /// cookie 的路径。默认 <c>/</c>。
    /// </summary>
    /// <remarks>
    /// 收窄到刷新端点所在的路径（如 <c>/api/auth</c>）可以让这枚 cookie 不再跟着每一个
    /// 业务请求发出去，是一个几乎无成本的收敛。默认留 <c>/</c> 是因为部署方可能改了
    /// 路由前缀或挂在 PathBase 下，写死一个更窄的值会让刷新在那些部署上直接失效。
    /// </remarks>
    public string CookiePath { get; set; } = "/";

    /// <summary>
    /// cookie 的域。默认 <c>null</c>（只发给当前主机）。跨子域共享会话时才需要设置。
    /// </summary>
    public string? CookieDomain { get; set; }

    /// <summary>
    /// cookie 的 SameSite 策略。默认 <see cref="SameSiteMode.Strict"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>Strict 是这套方案里的 CSRF 控制</b>：刷新端点匿名可达，若这枚 cookie 会跟着
    /// 跨站请求发出去，任意站点都能触发一次刷新。Strict 让它只在同站请求上出现。
    /// </para>
    /// <para>
    /// ⚠ 前端与 API 不同源时（独立的 api.example.com），Strict/Lax 都不会发送这枚 cookie，
    /// 必须改成 <see cref="SameSiteMode.None"/> 且启用 HTTPS，<b>并由应用自行补一层 CSRF 防护</b>
    /// （框架不替你做这个决定，因为跨源部署的形态差异太大）。
    /// </para>
    /// </remarks>
    public SameSiteMode SameSite { get; set; } = SameSiteMode.Strict;
}
