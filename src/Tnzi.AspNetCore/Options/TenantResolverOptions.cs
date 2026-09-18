namespace Tnzi.AspNetCore.Options;

/// <summary>
/// 租户解析来源
/// </summary>
public enum TenantResolutionSource
{
    /// <summary>HTTP Header</summary>
    Header = 0,
    /// <summary>查询字符串</summary>
    QueryString = 1,
    /// <summary>Cookie</summary>
    Cookie = 2,
    /// <summary>JWT Claims</summary>
    Claims = 3
}

/// <summary>
/// 租户解析中间件配置选项
/// 配置路径：AspNetCore:TenantResolver
/// </summary>
[ConfigSection("AspNetCore:TenantResolver")]
public class TenantResolverOptions
{
    /// <summary>
    /// 是否启用租户解析中间件（默认关闭）
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// HTTP Header 中的租户标识名
    /// </summary>
    public string HeaderName { get; set; } = "X-Tenant-Id";

    /// <summary>
    /// 查询字符串中的租户标识键
    /// </summary>
    public string QueryStringKey { get; set; } = "tenantId";

    /// <summary>
    /// Cookie 中的租户标识名
    /// </summary>
    public string CookieName { get; set; } = "TenantId";

    /// <summary>
    /// 框架约定的租户 claim 名。登录时 <c>JwtTokenService</c> 写它，<c>HttpContextCurrentUser.TenantId</c>
    /// 与 SignalR 的 <c>TnziHub</c>（按租户分组）读它 —— 三处必须同一个字符串，所以只从这里出。
    /// </summary>
    public const string DefaultClaimType = "tenant_id";

    /// <summary>
    /// Claims 中的租户标识键。
    /// </summary>
    /// <remarks>
    /// ★ 只有 <c>TenantResolverMiddleware</c> 读这个配置项；签发方与 <c>HttpContextCurrentUser</c> / <c>TnziHub</c>
    /// 都按 <see cref="DefaultClaimType"/> 常量写 / 读。改成别的值只会让中间件去找一个框架从不签发的 claim，
    /// 它是给「令牌由别家签发、claim 名不归框架定」的部署留的。
    /// </remarks>
    public string ClaimType { get; set; } = DefaultClaimType;

    /// <summary>
    /// 解析顺序（按列表顺序尝试，找到即停止）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★ <strong>出厂顺序把 <see cref="TenantResolutionSource.Claims"/> 放在最前。</strong>
    /// 此前 Header 打头，于是一个已登录用户只要加一个 <c>X-Tenant-Id</c> 就能让整条请求
    /// 在别的租户上下文里执行。中间件现在对已认证请求一律以 claim 为准
    /// （不符即 403），所以顺序本身不再是安全边界；但一个只看默认值就上线的部署
    /// 也应当拿到正确行为，而不是必须先读一遍文档。
    /// </para>
    /// <para>
    /// Header / QueryString / Cookie 三项仍然有用，它们服务的是<b>匿名</b>请求
    /// —— 按租户分流的登录页在拿到令牌之前没有 claim 可读。
    /// </para>
    /// </remarks>
    public List<TenantResolutionSource> ResolutionOrder { get; set; } =
    [
        TenantResolutionSource.Claims,
        TenantResolutionSource.Header,
        TenantResolutionSource.QueryString,
        TenantResolutionSource.Cookie
    ];

    /// <summary>
    /// 默认租户 ID（所有来源均未解析到时使用）
    /// </summary>
    public Guid? DefaultTenantId { get; set; }
}
