using Microsoft.AspNetCore.WebUtilities;

namespace Tnzi.AspNetCore.Middleware;

/// <summary>
/// 请求体 / 响应体进日志之前的最后一道：哪些路径一律不采，采了的按敏感字段名脱敏。
/// </summary>
/// <remarks>
/// <para>
/// 两个把体写进日志的地方共用这一份：请求日志（<see cref="RequestTrackingMiddleware"/>）与异常诊断
/// （<see cref="ExceptionHandlingMiddleware"/>）。各写一份的结果是「一处脱敏、另一处明文」——
/// 两个开关都能在配置中心热开，打开异常诊断后登录途中的一次异常就会把密码原样写进日志。
/// </para>
/// <para>
/// 脱敏按字段名做，覆盖 JSON 与表单编码两种形态；其余文本（XML、纯文本）无从按字段识别，原样保留，
/// 这是 <c>LogRequestBody</c> 开关的已知代价。
/// </para>
/// </remarks>
internal static class LoggedBodySanitizer
{
    private static readonly RequestBodyRedactor JsonRedactor = new();

    /// <summary>
    /// 无论开关如何，这些路径下的请求体与响应体一律不采集。
    /// </summary>
    /// <remarks>
    /// 脱敏是按字段名做的，而认证端点上「哪个字段是凭据」并不总是猜得到：OAuth 回调返回整页 HTML、
    /// 验证码端点返回图片的 base64、错误信封里带临时令牌的细节对象。这些端点的体没有一个字段是排障
    /// 非看不可的，而其中随便哪一个泄漏都等于账号失守，所以整条路径不采集。
    /// </remarks>
    private static readonly string[] ExcludedPathSegments =
    [
        "/auth/", "/connect/"
    ];

    /// <summary>
    /// 表单体的敏感键：JSON 字段名（camelCase）与查询参数名（OAuth 的 snake_case）两份名单的并集 ——
    /// 表单两种写法都有（<c>password</c>、<c>client_secret</c>）。
    /// </summary>
    private static readonly IReadOnlyCollection<string> FormSensitiveKeys =
        new HashSet<string>(
            RequestBodyRedactor.DefaultSensitiveFields.Concat(QueryStringRedactor.DefaultSensitiveKeys).Append("client_secret"),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>这条请求的体是否允许采集。</summary>
    public static bool AllowsBodyCapture(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        // 尾部补一个 '/'，让 "/api/auth" 这种不带尾斜杠的写法也命中 "/auth/"。
        var probe = value.EndsWith('/') ? value : value + "/";
        foreach (var segment in ExcludedPathSegments)
        {
            if (probe.Contains(segment, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 按敏感字段名脱敏一段体：表单编码按键脱敏，其余按 JSON 脱敏（不是合法 JSON 时原样返回）。
    /// </summary>
    public static string? Redact(string? body, string? contentType = null)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return body;
        }

        // 标成表单、实为 JSON 的体（客户端标错类型很常见）仍按 JSON 脱敏：表单解析会把整段 JSON 当成一个键。
        var trimmed = body.TrimStart();
        var looksLikeJson = trimmed.StartsWith('{') || trimmed.StartsWith('[');
        if (!looksLikeJson && IsFormUrlEncoded(contentType))
        {
            var parsed = QueryHelpers.ParseQuery(body);
            var redacted = QueryStringRedactor.Redact(parsed, body, FormSensitiveKeys);

            // 命中时重建的串带前导 '?'（查询串形态），表单体没有它。
            return ReferenceEquals(redacted, body) ? body : redacted?.TrimStart('?');
        }

        return JsonRedactor.Redact(body, RequestBodyRedactor.DefaultSensitiveFields);
    }

    private static bool IsFormUrlEncoded(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var separator = contentType.IndexOf(';');
        var mediaType = (separator >= 0 ? contentType[..separator] : contentType).Trim();
        return mediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
    }
}
