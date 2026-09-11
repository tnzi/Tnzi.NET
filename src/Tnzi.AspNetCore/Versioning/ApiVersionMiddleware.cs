
namespace Tnzi.AspNetCore.Versioning;

/// <summary>
/// API 版本中间件
/// 支持从 Header、QueryString、URL 路径中读取 API 版本
/// </summary>
public class ApiVersionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<AspNetCoreOptions> _options;
    private readonly ILogger<ApiVersionMiddleware> _logger;

    /// <summary>
    /// Regex 缓存，避免每次请求都重新编译正则表达式
    /// </summary>
    private static readonly ConcurrentDictionary<string, Regex> _regexCache = new();

    public ApiVersionMiddleware(
        RequestDelegate next,
        IOptionsMonitor<AspNetCoreOptions> options,
        ILogger<ApiVersionMiddleware> logger)
    {
        _next = Check.NotNull(next);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 版本号允许的字形：点分数字、字母、连字符、下划线，最长 32 字符。
    /// </summary>
    /// <remarks>
    /// ★★★ <strong>调用方给的版本串此前原样写回响应头。</strong>没有清单、没有长度限制、
    /// 也没有字符限制 —— 一个带 <c>%0d%0a</c> 的版本号会让 Kestrel 在写头时抛异常，
    /// 于是<b>任何人都能让任意一个请求变成 500</b>；超长的版本号则被逐字塞进每一条响应。
    /// 这条正则是「什么东西有资格出现在响应头里」的白名单，
    /// 而不是「什么东西是危险的」的黑名单 —— 后者永远少写一个字符。
    /// </remarks>
    private static readonly Regex WellFormedVersion = new(
        @"^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public async Task InvokeAsync(HttpContext context)
    {
        Check.NotNull(context);

        var aspNetCoreOptions = _options.CurrentValue;
        var apiVersionOptions = aspNetCoreOptions.ApiVersion;

        if (apiVersionOptions?.Enabled != true)
        {
            await _next(context);
            return;
        }

        var requested = ReadApiVersion(context, apiVersionOptions);
        var version = apiVersionOptions.DefaultVersion;

        if (!string.IsNullOrEmpty(requested))
        {
            if (!WellFormedVersion.IsMatch(requested))
            {
                // ★ 刻意回落到默认值而不是 400：UrlPath 模式下版本是从**路径**里正则抠出来的，
                //   一条碰巧含有 /v/... 的普通路径会被误读成版本号 —— 为它拒绝整个请求，
                //   代价比收益大。带一条 Warning，好过悄悄地按默认版本回答。
                _logger.LogWarning(
                    "Ignored a malformed API version on {Path}; falling back to the default version", context.Request.Path);
            }
            else if (!IsSupported(requested, apiVersionOptions))
            {
                // 配了清单却对不上，就是客户端在跟一个不存在的版本说话。
                // 静默按默认版本回答，等于让它以为自己在跟 v2 讲话而服务端答的是 v1。
                await MiddlewareResults.WriteErrorAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    $"API version '{requested}' is not supported.",
                    "UNSUPPORTED_API_VERSION");
                return;
            }
            else
            {
                version = requested;
            }
        }

        context.Items["ApiVersion"] = version;

        // 只回写已经过校验的值。默认值同样要过一遍：配置也可能被写坏。
        if (apiVersionOptions.ReportVersion && !string.IsNullOrEmpty(version) && WellFormedVersion.IsMatch(version))
        {
            context.Response.Headers[apiVersionOptions.VersionHeaderName] = version;
        }

        await _next(context);
    }

    /// <summary>版本是否在受支持清单里；没配清单则一律受支持。</summary>
    private static bool IsSupported(string version, ApiVersionOptions options)
        => options.SupportedVersions is not { Length: > 0 } supported
           || supported.Contains(version, StringComparer.OrdinalIgnoreCase);

    private string? ReadApiVersion(HttpContext context, ApiVersionOptions options)
    {
        return options.ReaderType switch
        {
            ApiVersionReaderType.Header => ReadFromHeader(context, options),
            ApiVersionReaderType.QueryString => ReadFromQueryString(context, options),
            ApiVersionReaderType.UrlPath => ReadFromUrlPath(context, options),
            _ => null
        };
    }

    private string? ReadFromHeader(HttpContext context, ApiVersionOptions options)
    {
        if (context.Request.Headers.TryGetValue(options.HeaderName, out var version))
        {
            return version.ToString();
        }
        return null;
    }

    private string? ReadFromQueryString(HttpContext context, ApiVersionOptions options)
    {
        if (context.Request.Query.TryGetValue(options.QueryStringName, out var version))
        {
            return version.ToString();
        }
        return null;
    }

    private static string? ReadFromUrlPath(HttpContext context, ApiVersionOptions options)
    {
        var path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var regex = _regexCache.GetOrAdd(options.UrlParameterName, paramName =>
            new Regex($"/{Regex.Escape(paramName)}/([^/]+)", RegexOptions.Compiled));
        var match = regex.Match(path);

        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return null;
    }
}