
namespace Tnzi.AspNetCore.Http;

/// <summary>
/// HTTP上下文扩展方法
/// </summary>
public static class HttpContextExtensions
{
    /// <summary>
    /// 确定指定的 HTTP 请求是否为 AJAX 请求
    /// </summary>
    /// <param name="request">HTTP 请求</param>
    /// <returns>如果指定的 HTTP 请求是 AJAX 请求, 则为 true；否则为 false</returns>
    public static bool IsAjaxRequest(this HttpRequest request)
    {
        Check.NotNull(request);

        return string.Equals(request.Query["X-Requested-With"], "XMLHttpRequest", StringComparison.Ordinal)
            || string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.Ordinal);
    }

    /// <summary>
    /// 确定指定的 HTTP 请求的 ContentType 是否为 JSON 方式
    /// </summary>
    /// <param name="request">HTTP 请求</param>
    /// <returns>如果 ContentType 为 JSON, 则为 true；否则为 false</returns>
    public static bool IsJsonContextType(this HttpRequest request)
    {
        Check.NotNull(request);

        var contentType = request.Headers["Content-Type"].ToString();
        if (!string.IsNullOrEmpty(contentType))
        {
            if (contentType.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) > -1 ||
                contentType.IndexOf("text/json", StringComparison.OrdinalIgnoreCase) > -1)
            {
                return true;
            }
        }

        var accept = request.Headers["Accept"].ToString();
        if (!string.IsNullOrEmpty(accept))
        {
            if (accept.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) > -1 ||
                accept.IndexOf("text/json", StringComparison.OrdinalIgnoreCase) > -1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判断是否为 API 请求
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="prefix">API 路径前缀，默认 "/api"</param>
    /// <returns>如果是 API 请求, 则为 true；否则为 false</returns>
    public static bool IsApiRequest(this HttpContext context, string? prefix = null)
    {
        Check.NotNull(context);

        prefix ??= "/api";
        return context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 获取客户端IP地址（支持反向代理场景）
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <returns>客户端IP地址</returns>
    public static string? GetClientIp(this HttpContext context)
    {
        Check.NotNull(context);

        return context.Request.GetClientIp();
    }

    /// <summary>
    /// 获取客户端IP地址（支持反向代理场景）
    /// </summary>
    /// <param name="request">HTTP 请求</param>
    /// <returns>
    /// 客户端IP地址；当 <see cref="AspNetCoreOptions.CollectClientIpAddress"/> 为 <c>false</c> 时恒为 <c>null</c>。
    /// </returns>
    /// <remarks>
    /// 这是全框架采集来源地址的唯一入口，因此隐私开关也判在这里：
    /// 关闭后请求日志、访问日志、审计上下文与限流一次性全部拿不到地址，
    /// 不需要每个消费者各自记得处理。见 <see cref="AspNetCoreOptions.CollectClientIpAddress"/>。
    /// </remarks>
    public static string? GetClientIp(this HttpRequest request)
    {
        Check.NotNull(request);

        if (!IsClientIpCollectionEnabled(request.HttpContext))
        {
            return null;
        }

        // 优先从 X-Forwarded-For 头获取（反向代理场景）
        var ipAddress = request.Headers["X-Forwarded-For"].FirstOrDefault();
        
        // 如果 X-Forwarded-For 存在多个IP（如：client, proxy1, proxy2），取第一个
        if (!string.IsNullOrEmpty(ipAddress))
        {
            var ips = ipAddress.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ips.Length > 0)
            {
                ipAddress = ips[0];
            }
        }

        // 如果 X-Forwarded-For 为空，尝试从 X-Real-IP 获取
        if (string.IsNullOrEmpty(ipAddress))
        {
            ipAddress = request.Headers["X-Real-IP"].FirstOrDefault();
        }

        // 最后从 Connection.RemoteIpAddress 获取
        if (string.IsNullOrEmpty(ipAddress))
        {
            ipAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString();
        }

        return ipAddress;
    }

    /// <summary>
    /// 判断当前部署是否允许采集来源地址。
    /// </summary>
    /// <remarks>
    /// 取不到配置时按<strong>允许</strong>处理：这是既有行为，
    /// 不能因为解析不到选项就静默改变一个已发布 API 的返回值。
    /// 真正要关闭采集的部署会显式配置它，而那种部署里选项一定解析得到。
    /// </remarks>
    private static bool IsClientIpCollectionEnabled(HttpContext context)
    {
        var options = context.RequestServices?.GetService<IOptionsMonitor<AspNetCoreOptions>>();
        return options?.CurrentValue.CollectClientIpAddress ?? true;
    }

    /// <summary>
    /// 获取 User-Agent
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <returns>User-Agent 字符串</returns>
    public static string? GetUserAgent(this HttpContext context)
    {
        Check.NotNull(context);

        return context.Request.GetUserAgent();
    }

    /// <summary>
    /// 获取 User-Agent
    /// </summary>
    /// <param name="request">HTTP 请求</param>
    /// <returns>User-Agent 字符串</returns>
    public static string? GetUserAgent(this HttpRequest request)
    {
        Check.NotNull(request);

        return request.Headers["User-Agent"].FirstOrDefault();
    }

    /// <summary>
    /// 获取客户端类型
    /// </summary>
    /// <param name="request">HTTP 请求</param>
    /// <returns>客户端类型</returns>
    public static RequestClientType GetClientType(this HttpRequest request)
    {
        Check.NotNull(request);

        var userAgent = request.GetUserAgent();

        if (string.IsNullOrEmpty(userAgent))
        {
            return RequestClientType.Unknown;
        }

        // 移动设备检测
        if (userAgent.Contains("android", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("iphone", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("ipad", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("ipod", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("mobile", StringComparison.OrdinalIgnoreCase))
        {
            return RequestClientType.Mobile;
        }

        // 桌面操作系统检测
        if (userAgent.Contains("windows nt", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("macintosh", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("x11", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("linux", StringComparison.OrdinalIgnoreCase))
        {
            return RequestClientType.Desktop;
        }

        // 浏览器检测
        if (userAgent.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("firefox", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("safari", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("opera", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("edge", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("msie", StringComparison.OrdinalIgnoreCase))
        {
            return RequestClientType.Browser;
        }

        return RequestClientType.Unknown;
    }

    /// <summary>
    /// 获取客户端语言偏好
    /// </summary>
    /// <param name="request">HTTP 请求</param>
    /// <returns>语言代码（如：zh-CN, en-US）</returns>
    public static string? GetAcceptLanguage(this HttpRequest request)
    {
        Check.NotNull(request);

        var acceptLanguage = request.Headers["Accept-Language"].FirstOrDefault();
        
        if (string.IsNullOrEmpty(acceptLanguage))
        {
            return null;
        }

        // 解析 Accept-Language 头（格式：zh-CN,zh;q=0.9,en;q=0.8）
        var languages = acceptLanguage.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (languages.Length > 0)
        {
            // 取第一个语言（优先级最高）
            var firstLang = languages[0];
            // 移除质量值（如：zh-CN;q=0.9 -> zh-CN）
            var lang = firstLang.Split(';')[0].Trim();
            return lang;
        }

        return null;
    }

    /// <summary>
    /// 获取请求参数（优先从 Query，其次从 Form）
    /// </summary>
    /// <param name="request">HTTP 请求</param>
    /// <param name="key">参数键名</param>
    /// <returns>参数值</returns>
    public static string? Params(this HttpRequest request, string key)
    {
        Check.NotNull(request);
        if (string.IsNullOrEmpty(key))
            return null;

        // 优先从 Query 获取
        if (request.Query.ContainsKey(key))
        {
            return request.Query[key];
        }

        // 其次从 Form 获取
        if (request.HasFormContentType && request.Form.ContainsKey(key))
        {
            return request.Form[key];
        }

        return null;
    }

    /// <summary>
    /// 获取请求追踪ID（如果存在）
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <returns>请求追踪ID</returns>
    public static string? GetRequestId(this HttpContext context)
    {
        Check.NotNull(context);

        // 优先从 HttpContext.Items 获取
        if (context.Items.TryGetValue("RequestId", out var requestId) && requestId is string id)
        {
            return id;
        }

        // 其次从请求头获取
        return context.Request.Headers["X-Request-Id"].FirstOrDefault()
            ?? context.Request.Headers["X-Trace-Id"].FirstOrDefault()
            ?? context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
    }

    /// <summary>
    /// 获取 ClaimsIdentity（如果用户已认证）
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <returns>ClaimsIdentity 或 null</returns>
    public static ClaimsIdentity? GetIdentity(this HttpContext context)
    {
        Check.NotNull(context);

        var user = context.User;
        if (user != null && user.Identity != null && user.Identity.IsAuthenticated && user.Identity is ClaimsIdentity identity)
        {
            return identity;
        }

        return null;
    }
}

/// <summary>
/// 请求客户端类型
/// </summary>
public enum RequestClientType
{
    /// <summary>
    /// 未知类型
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 移动设备
    /// </summary>
    Mobile = 1,

    /// <summary>
    /// 桌面操作系统
    /// </summary>
    Desktop = 2,

    /// <summary>
    /// 浏览器
    /// </summary>
    Browser = 3
}