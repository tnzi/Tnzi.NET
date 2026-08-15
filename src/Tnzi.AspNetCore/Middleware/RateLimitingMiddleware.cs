
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
    /// 处理请求。IRateLimitService 是 scoped 服务，约定中间件本身是单例 -
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
            // 拿不到分区键：无法把请求归到任何额度上，按配置处置。
            // 默认放行是为了兼容既有行为，但那意味着限流在此静默失效，故记一条警告。
            switch (options.MissingPartitionKey)
            {
                case MissingPartitionKeyBehavior.Deny:
                    // 必须记日志：这条 429 与「真的超限」在响应上完全一样，
                    // 不记的话运维分不清是配额用尽还是分区判定失灵，而后者是要去修的。
                    _logger.LogWarning(
                        "Request rejected: no rate limit partition key available - Path: {Path}. "
                        + "Register an IRateLimitPartitionKeyProvider if anonymous requests must be served.",
                        context.Request.Path);
                    await WriteRateLimitedAsync(context, ResolveWindowSeconds(rule, options));
                    return;

                case MissingPartitionKeyBehavior.Global:
                    key = $"global:{matchedPath ?? context.Request.Path.Value ?? string.Empty}";
                    break;

                default:
                    _logger.LogWarning(
                        "Rate limiting skipped: no partition key available for an anonymous request - Path: {Path}. "
                        + "Configure AspNetCore:RateLimit:MissingPartitionKey or register an IRateLimitPartitionKeyProvider.",
                        context.Request.Path);
                    await _next(context);
                    return;
            }
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
                // IP 由 GetClientIp 统一判定是否采集，关闭采集的部署这里自然是空，
                // 不需要在日志处再判一次（见 AspNetCoreOptions.CollectClientIpAddress）。
                _logger.LogWarning(
                    "Rate limit exceeded - Key: {Key}, Count: {Count}, Limit: {Limit}, Window: {Window} seconds, Path: {Path}, IP: {IP}",
                    key, count, rule.Limit, rule.WindowSeconds, context.Request.Path, context.Request.GetClientIp());

                await WriteRateLimitedAsync(context, rule.WindowSeconds);
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
    /// 写出 429 响应。
    /// </summary>
    private static async Task WriteRateLimitedAsync(HttpContext context, int retryAfterSeconds)
    {
        context.Response.StatusCode = 429; // Too Many Requests
        context.Response.ContentType = "application/json";
        context.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();

        var errorResult = ApiResult.Error("Rate limit exceeded. Please try again later.", 429);
        await context.Response.WriteAsync(JsonSerializer.Serialize(errorResult, TnziJsonDefaults.Options));
    }

    /// <summary>
    /// 取窗口秒数，规则缺失时回退默认值（用于分区键缺失且配置为拒绝的分支）。
    /// </summary>
    private static int ResolveWindowSeconds(RateLimitRule? rule, RateLimitOptions options)
        => rule?.WindowSeconds ?? (options.DefaultWindowSeconds > 0 ? options.DefaultWindowSeconds : 60);

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
    /// 获取限流键。
    /// </summary>
    /// <remarks>
    /// 分区来源依次是：注册的 <see cref="IRateLimitPartitionKeyProvider"/>、已登录用户、来源地址。
    /// 三者都拿不到时返回空串，由调用方按 <see cref="RateLimitOptions.MissingPartitionKey"/> 处置。
    /// <para>
    /// 自定义提供者排在最前面，是为了让「不采集来源地址」的部署有办法在不牺牲限流的前提下成立；
    /// 排在用户之前而不是之后，则是因为一个部署既然给出了自己的分区维度，就应当由它说了算。
    /// </para>
    /// </remarks>
    private static string GetRateLimitKey(HttpContext context, RateLimitRule? rule, string? matchedPath = null)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var keyPath = matchedPath ?? path; // 使用匹配的路径，如果没有则使用完整路径

        // 优先使用自定义分区（未注册任何提供者时 DI 给出空集合，不产生开销）
        var providers = context.RequestServices.GetServices<IRateLimitPartitionKeyProvider>();
        foreach (var provider in providers.OrderBy(p => p.Order))
        {
            var partition = provider.GetPartitionKey(context);
            if (!string.IsNullOrEmpty(partition))
            {
                return $"{partition}:{keyPath}";
            }
        }

        // 其次使用用户ID（如果已认证）
        var currentUser = context.RequestServices.GetService<ICurrentUser>();
        if (currentUser?.Id != null)
        {
            return $"user:{currentUser.Id}:{keyPath}";
        }

        // 最后使用来源地址；部署关闭采集时这里恒为空
        var ip = context.Request.GetClientIp();
        if (string.IsNullOrEmpty(ip))
        {
            return string.Empty;
        }

        return $"ip:{ip}:{keyPath}";
    }
}