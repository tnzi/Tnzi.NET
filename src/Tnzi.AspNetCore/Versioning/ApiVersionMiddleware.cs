
namespace Tnzi.AspNetCore.Versioning;

/// <summary>
/// API 版本中间件
/// 支持从 Header、QueryString、URL 路径中读取 API 版本
/// </summary>
public class ApiVersionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<AspNetCoreOptions> _options;

    /// <summary>
    /// Regex 缓存，避免每次请求都重新编译正则表达式
    /// </summary>
    private static readonly ConcurrentDictionary<string, Regex> _regexCache = new();

    public ApiVersionMiddleware(
        RequestDelegate next,
        IOptionsMonitor<AspNetCoreOptions> options)
    {
        _next = Check.NotNull(next);
        _options = Check.NotNull(options);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var aspNetCoreOptions = _options.CurrentValue;
        var apiVersionOptions = aspNetCoreOptions.ApiVersion;

        if (apiVersionOptions?.Enabled != true)
        {
            await _next(context);
            return;
        }

        var version = ReadApiVersion(context, apiVersionOptions);

        if (string.IsNullOrEmpty(version))
        {
            version = apiVersionOptions.DefaultVersion;
        }

        context.Items["ApiVersion"] = version;

        if (apiVersionOptions.ReportVersion)
        {
            context.Response.Headers[apiVersionOptions.VersionHeaderName] = version;
        }

        await _next(context);
    }

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