namespace Tnzi.AspNetCore.Middleware;

/// <summary>
/// 租户解析中间件
/// 按配置的解析链（Header → QueryString → Cookie → Claims）解析租户 ID，
/// 解析成功后通过 ICurrentTenant.Change() 切换租户上下文
/// </summary>
public class TenantResolverMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TenantResolverOptions _options;
    private readonly ILogger<TenantResolverMiddleware> _logger;

    public TenantResolverMiddleware(
        RequestDelegate next,
        IOptions<TenantResolverOptions> options,
        ILogger<TenantResolverMiddleware> logger)
    {
        _next = Check.NotNull(next);
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    public async Task InvokeAsync(HttpContext httpContext, ICurrentTenant currentTenant)
    {
        var tenantId = ResolveTenantId(httpContext);

        if (tenantId.HasValue)
        {
            _logger.LogDebug("Resolved tenant ID: {TenantId}", tenantId.Value);
            using (currentTenant.Change(tenantId.Value))
            {
                await _next(httpContext);
            }
        }
        else
        {
            await _next(httpContext);
        }
    }

    private Guid? ResolveTenantId(HttpContext httpContext)
    {
        foreach (var source in _options.ResolutionOrder)
        {
            var tenantId = source switch
            {
                TenantResolutionSource.Header => ResolveFromHeader(httpContext),
                TenantResolutionSource.QueryString => ResolveFromQueryString(httpContext),
                TenantResolutionSource.Cookie => ResolveFromCookie(httpContext),
                TenantResolutionSource.Claims => ResolveFromClaims(httpContext),
                _ => null
            };

            if (tenantId.HasValue) return tenantId;
        }

        return _options.DefaultTenantId;
    }

    private Guid? ResolveFromHeader(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue(_options.HeaderName, out var headerValue)
            && Guid.TryParse(headerValue, out var tenantId))
        {
            return tenantId;
        }
        return null;
    }

    private Guid? ResolveFromQueryString(HttpContext httpContext)
    {
        if (httpContext.Request.Query.TryGetValue(_options.QueryStringKey, out var queryValue)
            && Guid.TryParse(queryValue, out var tenantId))
        {
            return tenantId;
        }
        return null;
    }

    private Guid? ResolveFromCookie(HttpContext httpContext)
    {
        if (httpContext.Request.Cookies.TryGetValue(_options.CookieName, out var cookieValue)
            && Guid.TryParse(cookieValue, out var tenantId))
        {
            return tenantId;
        }
        return null;
    }

    private Guid? ResolveFromClaims(HttpContext httpContext)
    {
        var claimValue = httpContext.User?.FindFirst(_options.ClaimType)?.Value;
        if (claimValue != null && Guid.TryParse(claimValue, out var tenantId))
        {
            return tenantId;
        }
        return null;
    }
}
