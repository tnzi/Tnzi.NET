
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

        // 分区键的路径维度：ByPath 命中时是配置的前缀，否则是请求命中的端点（见 ResolveKeyPath）
        var keyPath = ResolveKeyPath(context, matchedPath);
        var key = GetRateLimitKey(context, keyPath);
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
                        context.GetRedactedPath());
                    await WriteRateLimitedAsync(context, ResolveWindowSeconds(rule, options));
                    return;

                case MissingPartitionKeyBehavior.Global:
                    key = $"global:{keyPath}";
                    break;

                default:
                    _logger.LogWarning(
                        "Rate limiting skipped: no partition key available for an anonymous request - Path: {Path}. "
                        + "Configure AspNetCore:RateLimit:MissingPartitionKey or register an IRateLimitPartitionKeyProvider.",
                        context.GetRedactedPath());
                    await _next(context);
                    return;
            }
        }

        // 检查白名单（用户 ID 与来源地址任一命中即放行）
        if (IsWhitelisted(context, rule, options))
        {
            await _next(context);
            return;
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
                    key, count, rule.Limit, rule.WindowSeconds, context.GetRedactedPath(), context.Request.GetClientIp());

                await WriteRateLimitedAsync(context, rule.WindowSeconds);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Rate limiting middleware error - Path: {Path}, Method: {Method}",
                context.GetRedactedPath(), context.Request.Method);

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
        context.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();

        await MiddlewareResults.WriteErrorAsync(
            context, StatusCodes.Status429TooManyRequests, "Rate limit exceeded. Please try again later.", "RATE_LIMITED");
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
    /// 判断请求是否命中白名单。
    /// </summary>
    /// <remarks>
    /// 一个请求带两个标识：已登录用户的 ID 与来源地址。白名单条目可以是其中任何一种，
    /// 任一命中即放行 —— 只比对一个标识的话，限流挪到认证之后（2026-09-12）会让
    /// 已登录请求的标识从地址变成用户 ID，于是 <c>ByIp</c> 里写的内部主机地址对带凭据的
    /// 调用方（监控、网关、服务账号）不再命中：配置还在、启动无告警，症状是白名单地址收到 429。
    /// <para>
    /// 参与比对的除了选中规则自己的白名单，还有 <c>ByIp</c> 的白名单（当选中的是 <c>ByUser</c> 时）：
    /// 在 <c>ByUser</c> 真正生效之前，同时配了两条规则的部署里 <c>ByIp</c> 的地址白名单
    /// 一直对每一个调用方生效，<c>ByUser</c> 接管已登录请求不该让它跟着消失 ——
    /// 地址是请求的属性，不是某条规则的属性。
    /// </para>
    /// </remarks>
    private static bool IsWhitelisted(HttpContext context, RateLimitRule rule, RateLimitOptions options)
    {
        var whitelists = new List<string[]>(2);
        if (rule.Whitelist is { Length: > 0 })
        {
            whitelists.Add(rule.Whitelist);
        }

        if (ReferenceEquals(rule, options.ByUser) && options.ByIp?.Whitelist is { Length: > 0 } addressWhitelist)
        {
            whitelists.Add(addressWhitelist);
        }

        if (whitelists.Count == 0)
        {
            return false;
        }

        var userId = context.RequestServices.GetService<ICurrentUser>()?.Id?.ToString();
        var clientIp = context.Request.GetClientIp();
        var clientAddress = ParseAddress(clientIp);

        return whitelists.Any(whitelist => whitelist.Any(entry =>
            (userId != null && string.Equals(entry, userId, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrEmpty(clientIp) && string.Equals(entry, clientIp, StringComparison.OrdinalIgnoreCase))
            || (clientAddress != null && clientAddress.Equals(ParseAddress(entry)))));
    }

    /// <summary>
    /// 把地址文本解析成可比较的 <see cref="IPAddress"/>：IPv4 映射的 IPv6（<c>::ffff:10.0.0.5</c>）还原成 IPv4。
    /// </summary>
    /// <remarks>
    /// 双栈监听（<c>http://*:port</c>、<c>[::]</c>、容器默认的 <c>ASPNETCORE_HTTP_PORTS</c>）下 IPv4 客户端呈现为
    /// IPv4 映射地址，按字符串比对时白名单里写的 <c>10.0.0.5</c> 永不命中，探针照样收到 429。
    /// ★ 只对 <see cref="IPAddress.IsIPv4MappedToIPv6"/> 做 <c>MapToIPv4</c>：对原生 IPv6 地址调用它不抛异常，
    /// 而是安静地取低 32 位，一个恰好低位相同的 IPv6 地址就会被当成白名单里的 IPv4 主机。
    /// </remarks>
    private static IPAddress? ParseAddress(string? text)
    {
        // 不含 '.' 或 ':' 的条目是用户 ID：IPAddress.TryParse 会把纯数字 "42" 读成 0.0.0.42
        if (string.IsNullOrWhiteSpace(text)
            || text.AsSpan().IndexOfAny('.', ':') < 0
            || !IPAddress.TryParse(text.Trim(), out var address))
        {
            return null;
        }

        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
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
    private static string GetRateLimitKey(HttpContext context, string keyPath)
    {
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

    /// <summary>
    /// 取分区键里的路径维度：额度按端点算，不按请求写成的那串 URL 算。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 依次是：命中的 <c>ByPath</c> 前缀（配置值本身）、请求命中的路由端点的模板、
    /// 规范化后的请求路径（小写、去掉末尾斜杠）。
    /// </para>
    /// <para>
    /// <strong>为什么不能用原始路径。</strong>路由对字面段不分大小写、忽略末尾斜杠，而限流存储的键区分大小写。
    /// 原始路径会把同一个端点拆成无数个桶：<c>/api/auth/login</c>、<c>/api/auth/Login</c>、
    /// <c>/API/auth/login/</c> 打到同一个 action，却各算各的额度（n 个字母就是 2^n 种写法）；
    /// <c>/api/users/1</c>、<c>/api/users/2</c> 每个 id 一个新桶。于是只配了 <c>ByIp</c> / <c>ByUser</c> /
    /// <c>DefaultLimit</c> 的部署，限流对一个肯换写法的调用方永远不触发。
    /// 规范化只会合并桶（更严），不会拆开桶。
    /// </para>
    /// <para>
    /// 端点由路由中间件在管线最前面选出（<c>WebApplication</c> 隐式插入的 <c>UseRouting</c>；
    /// <c>UsePathBase</c> 之后会重新路由），所以限流跑到这里时已经可见。取不到端点
    /// （没有匹配的 404、静态文件、未启用端点路由的宿主）时退到规范化路径。
    /// </para>
    /// </remarks>
    private static string ResolveKeyPath(HttpContext context, string? matchedPath)
        => matchedPath
           ?? context.GetEndpointRouteTemplate()
           ?? NormalizePath(context.Request.Path.Value);

    /// <summary>
    /// 没有命中端点时的路径维度：小写、去掉末尾斜杠，与路由的匹配口径一致。
    /// </summary>
    private static string NormalizePath(string? path)
    {
        var trimmed = (path ?? string.Empty).TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed.ToLowerInvariant();
    }
}