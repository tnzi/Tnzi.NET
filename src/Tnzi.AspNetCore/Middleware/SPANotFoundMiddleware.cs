namespace Tnzi.AspNetCore.Middleware;

/// <summary>
/// SPA 404 处理中间件，对于 SPA 应用，非 API 路由的 404 请求回落到 index.html
/// </summary>
/// <remarks>
/// ★★★ <strong>刻意<em>不</em>重放管线。</strong>旧实现把路径改写成 <c>/index.html</c>、
/// 状态码改成 200，然后再走一次 <c>_next</c> —— 而端点在第一趟就已经选完
/// （选不中即为 <c>null</c>），重放不会重新路由。下游若没有静态文件中间件，
/// 就没有任何人处理这个请求，客户端拿到的是 <b>200 加一个空体</b>：
/// 浏览器上是白屏，读起来像应用坏了，比原本那个 404 更难诊断。
/// 现在直接从 web root 读 <c>index.html</c> 写出去，结果不再取决于下游注册了什么。
/// </remarks>
public class SPANotFoundMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SPANotFoundMiddleware> _logger;
    private readonly string _apiPathPrefix;

    /// <summary>SPA 应用外壳的文件名。</summary>
    private const string IndexFileName = "index.html";

    private const string IndexPath = "/" + IndexFileName;

    public SPANotFoundMiddleware(
        RequestDelegate next,
        IOptions<AspNetCoreOptions> options,
        IWebHostEnvironment environment,
        ILogger<SPANotFoundMiddleware> logger)
    {
        _next = Check.NotNull(next);
        _environment = Check.NotNull(environment);
        _logger = Check.NotNull(logger);

        var aspNetCoreOptions = options?.Value ?? new AspNetCoreOptions();
        _apiPathPrefix = aspNetCoreOptions.ApiPathPrefix ?? "/api";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        Check.NotNull(context);

        await _next(context);

        if (context.Response.StatusCode != StatusCodes.Status404NotFound || !ShouldFallBack(context))
        {
            return;
        }

        var shell = _environment.WebRootFileProvider?.GetFileInfo(IndexFileName);
        if (shell is not { Exists: true })
        {
            // ★ 找不到外壳时保留 404。回落成一个空的 200 会把「这个应用没有前端」
            //   伪装成「这个页面加载失败了」，而前者是一句准确的答案。
            _logger.LogDebug(
                "SPA fallback skipped for {Path}: {File} is not present in the web root", context.Request.Path, IndexFileName);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength = shell.Length;
        // 外壳引用的是带 hash 的分块名，必须每次重新验证：缓存住旧外壳，发版后刷新回来的
        // 仍是指向已删分块的那一份，前端的发版检测与分块恢复因此永远回不到新版本。
        // 带 hash 的分块本身可以长期缓存，那是静态文件中间件的事，这里不管。
        context.Response.Headers.CacheControl = "no-cache";

        await using var stream = shell.CreateReadStream();
        await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    /// <summary>这条 404 该不该回落到应用外壳。</summary>
    private bool ShouldFallBack(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // API 路由的 404 就是 404：把它换成一份 HTML，会让调用方在解析 JSON 时才发现问题。
        if (path.StartsWith(_apiPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 带扩展名的是静态资源。缺失的图片必须仍然是 404 —— 回落成 HTML 会让
        // 「资源没打包进去」表现为一个能加载但内容不对的文件。
        if (Path.HasExtension(path))
        {
            return false;
        }

        // 外壳自己 404 时不再回落到自己。
        if (path.Equals(IndexPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 响应已经开始发送就改不了了。
        return !context.Response.HasStarted;
    }
}
