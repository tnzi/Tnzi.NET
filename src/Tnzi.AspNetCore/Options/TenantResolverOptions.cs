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
    /// Claims 中的租户标识键
    /// </summary>
    public string ClaimType { get; set; } = "tenant_id";

    /// <summary>
    /// 解析顺序（按列表顺序尝试，找到即停止）
    /// </summary>
    public List<TenantResolutionSource> ResolutionOrder { get; set; } =
    [
        TenantResolutionSource.Header,
        TenantResolutionSource.QueryString,
        TenantResolutionSource.Cookie,
        TenantResolutionSource.Claims
    ];

    /// <summary>
    /// 默认租户 ID（所有来源均未解析到时使用）
    /// </summary>
    public Guid? DefaultTenantId { get; set; }
}
