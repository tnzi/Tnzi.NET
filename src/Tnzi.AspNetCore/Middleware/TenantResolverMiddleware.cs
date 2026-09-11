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
    private readonly ITenantChecker? _tenantChecker;

    public TenantResolverMiddleware(
        RequestDelegate next,
        IOptions<TenantResolverOptions> options,
        ILogger<TenantResolverMiddleware> logger,
        ITenantChecker? tenantChecker = null)
    {
        _next = Check.NotNull(next);
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
        _tenantChecker = tenantChecker;
    }

    public async Task InvokeAsync(HttpContext httpContext, ICurrentTenant currentTenant)
    {
        // ★★★ 已认证的请求，租户只能由令牌里的 claim 决定。
        //
        // 本中间件跑在 UseAuthentication() 之后，所以主体与它的 tenant_id 都已就绪 ——
        // 此前这里两个都没看，只查了「这个租户存不存在、启没启用」。而当时的默认解析顺序是
        // Header 优先于 Claims，于是 A 租户的用户带一个 X-Tenant-Id: B 发过来，
        // 后面的 EF 全局过滤器（GetCurrentTenantId 先读 ICurrentTenant）就整条请求
        // 按 B 租户执行 —— 读得到 B 的数据，写进去的新行也落在 B 名下。
        //
        // ★★ 比对的是**外部来源**（header / 查询串 / cookie），而不是 ResolveTenantId 的结果。
        // 这一点很容易写错：把 Claims 排到解析链最前之后，ResolveTenantId 在 claim 那里就停了，
        // 于是异租户的 header 根本不会被看见 —— 结果是安全的（claim 赢），但一次真实的越权尝试
        // 会长得和一次正常请求一模一样，风控与审计都看不到。而这道检查<b>最有价值的产物</b>
        // 恰恰是那条日志。所以外部来源要单独解析一次。
        //
        // 三种情形：
        //   · 有 claim 且外部来源与之不符 → 403（并留一条 Warning）。
        //   · 有 claim 且外部来源相符或没给 → 用 claim。
        //   · 无 claim（不绑租户的用户）→ 忽略外部来源，回落到 DefaultTenantId。
        //     否则「没有租户」的账号可以自己挑一个租户进去，那是同一个洞的另一半。
        //
        // 匿名请求不受此约束：按租户分流的登录页在拿到令牌之前只能靠 header / 域名。
        Guid? tenantId;
        if (httpContext.User?.Identity?.IsAuthenticated == true)
        {
            var claimTenantId = ResolveFromClaims(httpContext);
            var externalTenantId = ResolveExternalTenantId(httpContext);

            if (claimTenantId is null)
            {
                if (externalTenantId.HasValue)
                {
                    _logger.LogWarning(
                        "Ignored an externally supplied tenant ID on a request whose principal carries no tenant claim.");
                }
                tenantId = _options.DefaultTenantId;
            }
            else if (externalTenantId.HasValue && externalTenantId.Value != claimTenantId.Value)
            {
                _logger.LogWarning(
                    "Rejected a cross-tenant request: the principal belongs to {ClaimTenantId} but the request asked for {RequestedTenantId}.",
                    claimTenantId.Value, externalTenantId.Value);

                await WriteErrorAsync(
                    httpContext,
                    StatusCodes.Status403Forbidden,
                    "The requested tenant does not match the authenticated principal.",
                    "TENANT_MISMATCH");
                return;
            }
            else
            {
                tenantId = claimTenantId;
            }
        }
        else
        {
            tenantId = ResolveTenantId(httpContext);
        }

        if (tenantId.HasValue)
        {
            if (_tenantChecker != null)
            {
                var isActive = await _tenantChecker.IsActiveAsync(tenantId.Value, httpContext.RequestAborted);
                if (!isActive)
                {
                    _logger.LogWarning("Rejected request with invalid or inactive tenant ID: {TenantId}", tenantId.Value);
                    await WriteErrorAsync(
                        httpContext,
                        StatusCodes.Status400BadRequest,
                        "Invalid or inactive tenant.",
                        "INVALID_TENANT");
                    return;
                }
            }

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

    /// <summary>写一条标准信封的失败响应。</summary>
    /// <remarks>
    /// ★ 经 <see cref="MiddlewareResults"/> 出，不再手拼匿名对象。手拼的那一版把业务错误码
    /// 叫作 <c>error</c>，而标准信封里它叫 <c>errorCode</c> —— 响应看起来完全正常
    /// （状态码对、message 对），只有前端那句按错误码分支的判断<b>永远取不到值</b>。
    /// </remarks>
    private static Task WriteErrorAsync(HttpContext httpContext, int statusCode, string message, string errorCode)
        => MiddlewareResults.WriteErrorAsync(httpContext, statusCode, message, errorCode);

    /// <summary>
    /// 只从<b>调用方可控</b>的来源解析租户（header / 查询串 / cookie），跳过 claim。
    /// </summary>
    /// <remarks>
    /// 存在的唯一理由是「这次请求有没有<b>要求</b>换一个租户」这个问题，
    /// 必须与「最终该用哪个租户」分开问 —— 见 <see cref="InvokeAsync"/> 里的说明。
    /// 按配置的 <see cref="TenantResolverOptions.ResolutionOrder"/> 顺序尝试，
    /// 这样部署方关掉某个来源（把它从列表里删掉）时，这里也不再认它。
    /// </remarks>
    private Guid? ResolveExternalTenantId(HttpContext httpContext)
    {
        foreach (var source in _options.ResolutionOrder)
        {
            var tenantId = source switch
            {
                TenantResolutionSource.Header => ResolveFromHeader(httpContext),
                TenantResolutionSource.QueryString => ResolveFromQueryString(httpContext),
                TenantResolutionSource.Cookie => ResolveFromCookie(httpContext),
                _ => null
            };

            if (tenantId.HasValue) return tenantId;
        }

        return null;
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
