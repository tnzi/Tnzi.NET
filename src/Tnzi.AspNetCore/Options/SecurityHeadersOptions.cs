namespace Tnzi.AspNetCore.Options;

/// <summary>
/// 安全头部选项
/// </summary>
public class SecurityHeadersOptions
{
    /// <summary>
    /// Content Security Policy
    /// </summary>
    public string ContentSecurityPolicy { get; set; } =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https:; font-src 'self' data:;";

    /// <summary>
    /// X-Frame-Options
    /// DENY, SAMEORIGIN, ALLOW-FROM
    /// </summary>
    public string XFrameOptions { get; set; } = "DENY";

    /// <summary>
    /// X-Content-Type-Options
    /// </summary>
    public string? XContentTypeOptions { get; set; } = "nosniff";

    /// <summary>
    /// X-XSS-Protection
    /// 0: Disabled, 1: Enabled with block mode
    /// </summary>
    public string? XXssProtection { get; set; } = "1; mode=block";

    /// <summary>
    /// Strict-Transport-Security
    /// </summary>
    public bool HstsEnabled { get; set; } = false;

    /// <summary>
    /// HSTS Max-Age (秒）
    /// </summary>
    public int HstsMaxAge { get; set; } = 31536000; // 365 days

    /// <summary>
    /// HSTS IncludeSubDomains
    /// </summary>
    public bool HstsIncludeSubDomains { get; set; } = false;

    /// <summary>
    /// HSTS Preload
    /// </summary>
    public bool HstsPreload { get; set; } = false;

    /// <summary>
    /// Referrer-Policy
    /// no-referrer, no-referrer-when-downgrade, same-origin, strict-origin, strict-origin-when-cross-origin, origin-when-cross-origin, unsafe-url
    /// </summary>
    public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";

    /// <summary>
    /// Permissions-Policy
    /// </summary>
    public string? PermissionsPolicy { get; set; }

    /// <summary>
    /// 是否启用安全头部
    /// </summary>
    public bool EnableSecurityHeaders { get; set; } = false;
}
