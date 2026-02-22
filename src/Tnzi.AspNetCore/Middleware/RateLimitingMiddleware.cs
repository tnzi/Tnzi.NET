
namespace Tnzi.AspNetCore.Middleware;

/// <summary>
/// 限流中间件
/// 支持基于 IP、用户和路径的限流
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimitOptions _options;
    private readonly IRateLimitService _rateLimitService;
    private readonly ILogger<RateLimitingMiddleware> _logger;

    /// <summary>
    /// 缓存按路径长度降序排序的规则列表，避免每次请求重新排序
    /// </summary>
    private readonly List<KeyValuePair<string, RateLimitRule>>? _sortedPathRules;

    /// <summary>
    /// 初始化一个<see cref="RateLimitingMiddleware"/>类型的新实例
    /// </summary>
    public RateLimitingMiddleware(
        RequestDelegate next,
        IOptions<AspNetCoreOptions> aspNetCoreOptions,
        IRateLimitService rateLimitService,
        ILogger<RateLimitingMiddleware> logger)
    {
        _next = Check.NotNull(next);
        _options = Check.NotNull(aspNetCoreOptions).Value.RateLimit ?? new RateLimitOptions();
        _rateLimitService = Check.NotNull(rateLimitService);
        _logger = Check.NotNull(logger);

        // 预排序路径规则，避免每次请求排序
        if (_options.ByPath != null && _options.ByPath.Count > 0)
        {
            _sortedPathRules = _options.ByPath
                .OrderByDescending(kvp => kvp.Key.Length)
                .ToList();
        }
    }

    /// <summary>
    /// 处理请求
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // 检查是否启用限流
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        // 检查路径是否需要限流
        if (ShouldExcludePath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // 获取限流规则（同时获取匹配的路径）
        var (rule, matchedPath) = GetRateLimitRuleWithPath(context);
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
            var count = await _rateLimitService.IncrementAndGetAsync(key, rule.WindowSeconds, rule.Algorithm);
            
            if (count > rule.Limit)
            {
                _logger.LogWarning(
                    "Rate limit exceeded - Key: {Key}, Count: {Count}, Limit: {Limit}, Window: {Window} seconds, Path: {Path}, IP: {IP}",
                    key, count, rule.Limit, rule.WindowSeconds, context.Request.Path, context.Request.GetClientIp());
                
                context.Response.StatusCode = 429; // Too Many Requests
                context.Response.ContentType = "application/json";
                context.Response.Headers["Retry-After"] = rule.WindowSeconds.ToString();
                
                // 使用更安全的 JSON 序列化
                var errorResponse = System.Text.Json.JsonSerializer.Serialize(new 
                { 
                    error = "Rate limit exceeded. Please try again later.",
                    retryAfter = rule.WindowSeconds
                });
                await context.Response.WriteAsync(errorResponse);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Rate limiting middleware error - Path: {Path}, Method: {Method}",
                context.Request.Path, context.Request.Method);
            
            // 根据配置决定故障时的策略
            if (_options.AllowOnFailure)
            {
                // 允许通过（fail-open）
                await _next(context);
            }
            else
            {
                // 拒绝请求（fail-safe）
                context.Response.StatusCode = 503; // Service Unavailable
                context.Response.ContentType = "application/json";
                var errorResponse = System.Text.Json.JsonSerializer.Serialize(new 
                { 
                    error = "Rate limiting service is temporarily unavailable. Please try again later."
                });
                await context.Response.WriteAsync(errorResponse);
            }
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// 检查路径是否排除限流
    /// </summary>
    private bool ShouldExcludePath(PathString path)
    {
        if (_options.ExcludePaths == null || _options.ExcludePaths.Length == 0)
            return false;

        var pathValue = path.Value ?? string.Empty;
        return _options.ExcludePaths.Any(excludePath => 
            pathValue.StartsWith(excludePath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 获取限流规则（同时返回匹配的路径）
    /// </summary>
    private (RateLimitRule? rule, string? matchedPath) GetRateLimitRuleWithPath(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // 优先检查路径限流（使用最长匹配原则，已预排序）
        if (_sortedPathRules != null)
        {
            foreach (var pathRule in _sortedPathRules)
            {
                if (path.StartsWith(pathRule.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return (pathRule.Value, pathRule.Key);
                }
            }
        }

        // 检查用户限流
        var currentUser = context.RequestServices.GetService<ICurrentUser>();
        if (_options.ByUser != null && currentUser?.Id != null)
        {
            return (_options.ByUser, null);
        }

        // 检查 IP 限流
        if (_options.ByIp != null)
        {
            return (_options.ByIp, null);
        }

        // 使用默认限流
        if (_options.DefaultLimit > 0)
        {
            return (new RateLimitRule
            {
                Limit = _options.DefaultLimit,
                WindowSeconds = _options.DefaultWindowSeconds
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