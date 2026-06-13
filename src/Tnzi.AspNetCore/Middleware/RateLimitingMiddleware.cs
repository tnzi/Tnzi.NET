
namespace Tnzi.AspNetCore.Middleware;

/// <summary>
/// 限流中间件
/// 支持基于 IP、用户和路径的限流
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<AspNetCoreOptions> _optionsMonitor;
    private readonly ILogger<RateLimitingMiddleware> _logger;

    /// <summary>
    /// 初始化一个<see cref="RateLimitingMiddleware"/>类型的新实例
    /// </summary>
    public RateLimitingMiddleware(
        RequestDelegate next,
        IOptionsMonitor<AspNetCoreOptions> optionsMonitor,
        ILogger<RateLimitingMiddleware> logger)
    {
        _next = Check.NotNull(next);
        _optionsMonitor = Check.NotNull(optionsMonitor);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 处理请求。IRateLimitService 是 scoped 服务，约定中间件本身是单例 —
    /// 必须经 InvokeAsync 参数按请求解析（构造注入会在 root provider 校验时失败）。
    /// </summary>
    public async Task InvokeAsync(HttpContext context, IRateLimitService rateLimitService)
    {
        var options = _optionsMonitor.CurrentValue.RateLimit ?? new RateLimitOptions();

        // 检查是否启用限流
        if (!options.Enabled)
        {
            await _next(context);
            return;
        }

        // 检查路径是否需要限流
        if (ShouldExcludePath(context.Request.Path, options))
        {
            await _next(context);
            return;
        }

        // 获取限流规则（同时获取匹配的路径）
        var (rule, matchedPath) = GetRateLimitRuleWithPath(context, options);
        if (rule == null)
        {
            await _next(context);
            return;
        }

        // 获取限流键（传入匹配的路径以保持一致性）
        var key = GetRateLimitKey(context, rule, matchedPath);
        if (string.IsNullOrEmpty(key))
        {
            await _next(context);
            return;
        }

        // 检查白名单
        if (rule.Whitelist != null && rule.Whitelist.Length > 0)
        {
            var currentUser = context.RequestServices.GetService<ICurrentUser>();
            var identifier = currentUser?.Id?.ToString() ?? context.Request.GetClientIp() ?? string.Empty;

            if (rule.Whitelist.Contains(identifier, StringComparer.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }
        }

        try
        {
            // 先递增计数（原子操作），然后检查是否超过限流
            // 这样可以避免竞态条件：两个请求同时检查都通过，然后都递增导致超过限制
            var count = await rateLimitService.IncrementAndGetAsync(key, rule.WindowSeconds, rule.Algorithm);

            if (count > rule.Limit)
            {
                _logger.LogWarning(
                    "Rate limit exceeded - Key: {Key}, Count: {Count}, Limit: {Limit}, Window: {Window} seconds, Path: {Path}, IP: {IP}",
                    key, count, rule.Limit, rule.WindowSeconds, context.Request.Path, context.Request.GetClientIp());

                context.Response.StatusCode = 429; // Too Many Requests
                context.Response.ContentType = "application/json";
                context.Response.Headers["Retry-After"] = rule.WindowSeconds.ToString();

                var errorResult = ApiResult.Error("Rate limit exceeded. Please try again later.", 429);
                await context.Response.WriteAsync(JsonSerializer.Serialize(errorResult, TnziJsonDefaults.Options));
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Rate limiting middleware error - Path: {Path}, Method: {Method}",
                context.Request.Path, context.Request.Method);

            // 根据配置决定故障时的策略
            if (options.AllowOnFailure)
            {
                // 允许通过（fail-open）
                await _next(context);
            }
            else
            {
                // 拒绝请求（fail-safe）
                context.Response.StatusCode = 503; // Service Unavailable
                context.Response.ContentType = "application/json";
                var errorResult = ApiResult.Error("Rate limiting service is temporarily unavailable. Please try again later.", 503);
                await context.Response.WriteAsync(JsonSerializer.Serialize(errorResult, TnziJsonDefaults.Options));
            }
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// 检查路径是否排除限流
    /// </summary>
    private static bool ShouldExcludePath(PathString path, RateLimitOptions options)
    {
        if (options.ExcludePaths == null || options.ExcludePaths.Length == 0)
            return false;

        var pathValue = path.Value ?? string.Empty;
        return options.ExcludePaths.Any(excludePath =>
            pathValue.StartsWith(excludePath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 获取限流规则（同时返回匹配的路径）
    /// </summary>
    private static (RateLimitRule? rule, string? matchedPath) GetRateLimitRuleWithPath(HttpContext context, RateLimitOptions options)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // 优先检查路径限流（使用最长匹配原则）
        if (options.ByPath != null && options.ByPath.Count > 0)
        {
            var sortedPathRules = options.ByPath.OrderByDescending(kvp => kvp.Key.Length);
            foreach (var pathRule in sortedPathRules)
            {
                if (path.StartsWith(pathRule.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return (pathRule.Value, pathRule.Key);
                }
            }
        }

        // 检查用户限流
        var currentUser = context.RequestServices.GetService<ICurrentUser>();
        if (options.ByUser != null && currentUser?.Id != null)
        {
            return (options.ByUser, null);
        }

        // 检查 IP 限流
        if (options.ByIp != null)
        {
            return (options.ByIp, null);
        }

        // 使用默认限流
        if (options.DefaultLimit > 0)
        {
            return (new RateLimitRule
            {
                Limit = options.DefaultLimit,
                WindowSeconds = options.DefaultWindowSeconds
            }, null);
        }

        return (null, null);
    }

    /// <summary>
    /// 获取限流键
    /// </summary>
    private static string GetRateLimitKey(HttpContext context, RateLimitRule? rule, string? matchedPath = null)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var keyPath = matchedPath ?? path; // 使用匹配的路径，如果没有则使用完整路径

        // 优先使用用户ID（如果已认证）
        var currentUser = context.RequestServices.GetService<ICurrentUser>();
        if (currentUser?.Id != null)
        {
            return $"user:{currentUser.Id}:{keyPath}";
        }

        // 否则使用 IP
        var ip = context.Request.GetClientIp();
        if (string.IsNullOrEmpty(ip))
        {
            return string.Empty;
        }

        return $"ip:{ip}:{keyPath}";
    }
}