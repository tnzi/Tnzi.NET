
namespace Tnzi.AspNetCore.Middleware;

/// <summary>
/// 安全头部中间件
/// 自动添加常见的安全响应头，防止 XSS、点击劫持等安全威胁
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<AspNetCoreOptions> _options;

    public SecurityHeadersMiddleware(
        RequestDelegate next,
        IOptionsMonitor<AspNetCoreOptions> options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var options = _options.CurrentValue.SecurityHeaders;

        if (options != null && options.EnableSecurityHeaders)
        {
            // Apply security headers
            ApplySecurityHeaders(context, options);
        }

        await _next(context);
    }

    /// <summary>
    /// 应用安全头部
    /// </summary>
    private void ApplySecurityHeaders(HttpContext context, SecurityHeadersOptions options)
    {
        // Content-Security-Policy
        if (!string.IsNullOrEmpty(options.ContentSecurityPolicy))
        {
            context.Response.Headers["Content-Security-Policy"] = options.ContentSecurityPolicy;
        }

        // X-Frame-Options
        if (!string.IsNullOrEmpty(options.XFrameOptions))
        {
            context.Response.Headers["X-Frame-Options"] = options.XFrameOptions;
        }

        // X-Content-Type-Options
        if (!string.IsNullOrEmpty(options.XContentTypeOptions))
        {
            context.Response.Headers["X-Content-Type-Options"] = options.XContentTypeOptions;
        }

        // X-XSS-Protection
        if (!string.IsNullOrEmpty(options.XXssProtection))
        {
            context.Response.Headers["X-XSS-Protection"] = options.XXssProtection;
        }

        // Strict-Transport-Security
        if (options.HstsEnabled && context.Request.IsHttps)
        {
            var maxAge = options.HstsMaxAge;
            var includeSubdomains = options.HstsIncludeSubDomains ? "; includeSubDomains" : "";
            var preload = options.HstsPreload ? "; preload" : "";

            context.Response.Headers["Strict-Transport-Security"] =
                $"max-age={maxAge}{includeSubdomains}{preload}";
        }

        // Referrer-Policy
        if (!string.IsNullOrEmpty(options.ReferrerPolicy))
        {
            context.Response.Headers["Referrer-Policy"] = options.ReferrerPolicy;
        }

        // Permissions-Policy
        if (!string.IsNullOrEmpty(options.PermissionsPolicy))
        {
            context.Response.Headers["Permissions-Policy"] = options.PermissionsPolicy;
        }
    }
}