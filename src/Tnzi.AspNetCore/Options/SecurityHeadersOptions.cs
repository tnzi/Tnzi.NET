namespace Tnzi.AspNetCore.Options;

/// <summary>
/// 安全头部选项
/// </summary>
[ConfigSection("AspNetCore:SecurityHeaders")]
[RuntimeSettingGroup(Key = "web-security-headers", Module = "Web", DisplayName = "Security Headers",
    I18nKey = "admin.modules.system.settings.groups.webSecurityHeaders",
    Icon = "mdi:shield-lock-outline", Order = 710, PermissionGroup = "system")]
public class SecurityHeadersOptions
{
    /// <summary>
    /// 是否启用安全头部
    /// </summary>
    [RuntimeSetting(Label = "Enable Security Headers", I18n = "admin.modules.system.settings.fields.enableSecurityHeaders",
        Type = SettingFieldType.Boolean)]
    public bool EnableSecurityHeaders { get; set; } = false;

    /// <summary>
    /// Content Security Policy
    /// </summary>
    [RuntimeSetting(Label = "Content-Security-Policy", I18n = "admin.modules.system.settings.fields.contentSecurityPolicy",
        Type = SettingFieldType.Text)]
    public string ContentSecurityPolicy { get; set; } =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https:; font-src 'self' data:;";

    /// <summary>
    /// X-Frame-Options
    /// DENY, SAMEORIGIN, ALLOW-FROM
    /// </summary>
    [RuntimeSetting(Label = "X-Frame-Options", I18n = "admin.modules.system.settings.fields.xFrameOptions")]
    public string XFrameOptions { get; set; } = "DENY";

    /// <summary>
    /// X-Content-Type-Options
    /// </summary>
    [RuntimeSetting(Label = "X-Content-Type-Options", I18n = "admin.modules.system.settings.fields.xContentTypeOptions")]
    public string? XContentTypeOptions { get; set; } = "nosniff";

    /// <summary>
    /// X-XSS-Protection
    /// 0: Disabled, 1: Enabled with block mode
    /// </summary>
    [RuntimeSetting(Label = "X-XSS-Protection", I18n = "admin.modules.system.settings.fields.xXssProtection")]
    public string? XXssProtection { get; set; } = "1; mode=block";

    /// <summary>
    /// Referrer-Policy
    /// no-referrer, no-referrer-when-downgrade, same-origin, strict-origin, strict-origin-when-cross-origin, origin-when-cross-origin, unsafe-url
    /// </summary>
    [RuntimeSetting(Label = "Referrer-Policy", I18n = "admin.modules.system.settings.fields.referrerPolicy")]
    public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";

    /// <summary>
    /// Strict-Transport-Security
    /// </summary>
    [RuntimeSetting(Label = "Enable HSTS", I18n = "admin.modules.system.settings.fields.hstsEnabled",
        Type = SettingFieldType.Boolean)]
    public bool HstsEnabled { get; set; } = false;

    /// <summary>
    /// HSTS Max-Age (秒）
    /// </summary>
    [RuntimeSetting(Label = "HSTS Max-Age (seconds)", I18n = "admin.modules.system.settings.fields.hstsMaxAge",
        Type = SettingFieldType.Int, Min = 0)]
    public int HstsMaxAge { get; set; } = 31536000; // 365 days

    /// <summary>
    /// HSTS IncludeSubDomains
    /// </summary>
    [RuntimeSetting(Label = "HSTS Include Subdomains", I18n = "admin.modules.system.settings.fields.securityHstsIncludeSubDomains",
        Type = SettingFieldType.Boolean,
        Description = "Apply the Strict-Transport-Security policy to all subdomains as well as the base domain.")]
    public bool HstsIncludeSubDomains { get; set; } = false;

    /// <summary>
    /// HSTS Preload
    /// </summary>
    [RuntimeSetting(Label = "HSTS Preload", I18n = "admin.modules.system.settings.fields.securityHstsPreload",
        Type = SettingFieldType.Boolean,
        Description = "WARNING: enabling 'preload' signals intent to submit this domain to the browser HSTS preload list (hstspreload.org). Once preloaded, browsers hard-force HTTPS for this domain and every subdomain; removal is extremely slow and effectively irreversible (months of propagation, can hard-break access). Never enable in production unless you fully understand and accept this commitment.")]
    public bool HstsPreload { get; set; } = false;

    /// <summary>
    /// Permissions-Policy
    /// </summary>
    [RuntimeSetting(Label = "Permissions-Policy", I18n = "admin.modules.system.settings.fields.securityPermissionsPolicy",
        Type = SettingFieldType.Text,
        Description = "Permissions-Policy response header value controlling access to browser features (e.g. 'geolocation=(), camera=()'). Leave empty to omit the header.")]
    public string? PermissionsPolicy { get; set; }
}
