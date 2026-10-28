namespace Tnzi.Security;

/// <summary>
/// 把请求路径里作为凭据的路由参数值换成掩码；无状态。
/// </summary>
/// <remarks>
/// <para>
/// ★ 框架自己就把凭据放在路径里：<c>signing/{token}</c>（外部签署人的全部身份）、
/// <c>files/share/{token}</c>（拿到就能下载）。<see cref="QueryStringRedactor"/> 只管查询串，而请求日志、操作审计、访问日志
/// 三处此前都把路径原样记下，只持有「看日志」权限的运维就能从里面捡到别人的令牌。
/// </para>
/// <para>
/// 邀请令牌与登录待办的临时令牌不在路径里：那几个端点把令牌放在请求体里（<c>{ token }</c> / <c>{ tempToken }</c>），
/// 由 <see cref="RequestBodyRedactor"/> 的字段名单掩掉，本类管不到也不需要管。
/// </para>
/// <para>
/// 按<b>路由值</b>判定而不是按路径模板逐条列：路由参数名就是语义（<c>{token}</c>），新端点只要沿用这些名字就自动受保护，
/// 不必回头补名单。名单与 <see cref="QueryStringRedactor.DefaultSensitiveKeys"/> 是同一份：一个参数名是不是凭据，
/// 与它出现在查询串还是路径里无关。
/// </para>
/// <para>
/// 路由值只在路由匹配之后才有，故调用方应在路由之后的中间件里、或下游返回之后再取。
/// 没有匹配到端点的请求没有路由值，路径原样返回（它也没有消费任何令牌）。
/// </para>
/// <para>
/// 值先按整段比对（参数占满一个路径段的常见形状，转义与未转义的路径都认），整段对不上时再按子串替换
/// （参数只占段的一部分、或 catch-all 跨了多段）。ASP.NET Core 里的调用方用 <c>HttpContext.GetRedactedPath</c>。
/// </para>
/// </remarks>
public static class RequestPathRedactor
{
    /// <summary>
    /// 按路由值把 <paramref name="path"/> 里敏感参数对应的段换成 <see cref="QueryStringRedactor.RedactedValue"/>。
    /// </summary>
    /// <param name="path">请求路径，转义（<c>PathString.ToString()</c>）或未转义（<c>PathString.Value</c>）形式均可。</param>
    /// <param name="routeValues">本次请求的路由值（<c>HttpRequest.RouteValues</c>）。</param>
    /// <param name="sensitiveKeys">要脱敏的参数名（不区分大小写）。</param>
    /// <returns>脱敏后的路径；没有命中时返回 <paramref name="path"/> 本身。</returns>
    public static string? Redact(string? path, IEnumerable<KeyValuePair<string, object?>>? routeValues, IReadOnlyCollection<string> sensitiveKeys)
    {
        Check.NotNull(sensitiveKeys);

        if (string.IsNullOrEmpty(path) || routeValues is null || sensitiveKeys.Count == 0)
        {
            return path;
        }

        HashSet<string>? secrets = null;
        foreach (var pair in routeValues)
        {
            if (QueryStringRedactor.IsSensitive(pair.Key, sensitiveKeys)
                && Convert.ToString(pair.Value, CultureInfo.InvariantCulture) is { Length: > 0 } value)
            {
                (secrets ??= new HashSet<string>(StringComparer.Ordinal)).Add(value);
            }
        }

        if (secrets is null)
        {
            return path;
        }

        var segments = path.Split('/');
        var matched = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Length == 0)
            {
                continue;
            }

            // 路由值是解码后的；转义形式的路径要按解码结果比对。
            var hit = secrets.Contains(segment) ? segment
                : secrets.TryGetValue(Uri.UnescapeDataString(segment), out var decoded) ? decoded
                : null;
            if (hit != null)
            {
                segments[i] = QueryStringRedactor.RedactedValue;
                matched.Add(hit);
            }
        }

        var redacted = string.Join('/', segments);
        foreach (var secret in secrets)
        {
            if (matched.Contains(secret))
            {
                continue;
            }

            // 参数只占段的一部分（"{token}.pdf"）或 catch-all 跨了多段：按子串掩，转义形式一并掩。
            redacted = redacted.Replace(secret, QueryStringRedactor.RedactedValue, StringComparison.Ordinal);
            var escaped = Uri.EscapeDataString(secret);
            if (!string.Equals(escaped, secret, StringComparison.Ordinal))
            {
                redacted = redacted.Replace(escaped, QueryStringRedactor.RedactedValue, StringComparison.Ordinal);
            }
        }

        return string.Equals(redacted, path, StringComparison.Ordinal) ? path : redacted;
    }
}
