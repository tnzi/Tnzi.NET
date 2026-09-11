using Microsoft.Extensions.Primitives;

namespace Tnzi.Security;

/// <summary>
/// 把查询串里敏感参数的值换成掩码；无状态。
/// </summary>
/// <remarks>
/// <para>
/// ★ <strong>它住在核心程序集，是因为它有两个互不相识的消费方：</strong>
/// <c>Tnzi.AspNetCore</c> 的请求日志（<c>RequestTrackingMiddleware</c>）与 <c>Tnzi.Audit</c> 的操作审计
/// （<c>AuditMiddleware</c> 把 Path + QueryString 存进 <c>Audit_Operation.Url</c>，
/// 又把整个 Query 序列化进 <c>RequestParameters</c>）。此前只有前者脱敏，名单是它的私有字段，
/// 审计表于是把 <c>?token=</c>、<c>?enrollmentToken=</c>、<c>?sig=</c> 的原值一行行存了下来。
/// 各写一份名单的结果就是「某个流程比别处多露出一个键」—— 不报错，也不会让测试变红。
/// </para>
/// <para>
/// 与 <see cref="RequestBodyRedactor.DefaultSensitiveFields"/> <strong>刻意是两份名单</strong>：
/// 请求体字段名走 camelCase JSON 约定（<c>accessToken</c>），查询串参数名走 OAuth 的 snake_case 约定
/// （<c>access_token</c>），两边逐字不同。
/// </para>
/// <para>
/// 按已解析的 Query 集合<b>重建</b>而不是正则替换：值可能被 URL 编码、可能含 <c>&amp;</c>，
/// 只有按解析结果重建才不会漏也不会误伤。没有命中任何敏感参数时返回原串（常见路径零分配）。
/// </para>
/// </remarks>
public static class QueryStringRedactor
{
    /// <summary>敏感参数值的统一掩码。</summary>
    public const string RedactedValue = "***";

    /// <summary>
    /// 默认要脱敏的查询参数名（不区分大小写，精确匹配）。它们都是<b>凭据</b>：
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><c>access_token</c>：SignalR / SSE 传输携带的 JWT（浏览器 WebSocket 不能自定义请求头）</item>
    ///   <item><c>token</c>：邮件里的密码重置 / 邮箱确认链接</item>
    ///   <item><c>enrollmentToken</c>：passkey 注册的一次性令牌</item>
    ///   <item><c>sig</c>：签名文件链接的访问令牌</item>
    ///   <item><c>password</c>：分享链接口令</item>
    ///   <item><c>api_key</c> / <c>apiKey</c>：第三方回调与集成端点常见的密钥参数</item>
    /// </list>
    /// 没有收 <c>code</c>：OAuth 回调的授权码是一次性的，而按码查记录的列表接口比比皆是，
    /// 收进来会把它们的过滤条件一起抹掉。
    /// </remarks>
    public static IReadOnlyList<string> DefaultSensitiveKeys { get; } =
    [
        "access_token", "token", "enrollmentToken", "sig", "password", "api_key", "apiKey"
    ];

    /// <summary>判断一个参数名是否在敏感名单里（不区分大小写）。</summary>
    public static bool IsSensitive(string key, IReadOnlyCollection<string> sensitiveKeys)
    {
        Check.NotNull(sensitiveKeys);

        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        foreach (var sensitive in sensitiveKeys)
        {
            if (string.Equals(key, sensitive, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 按已解析的查询集合重建查询串，敏感参数的值换成 <see cref="RedactedValue"/>。
    /// </summary>
    /// <param name="query">已解析的查询集合（<see cref="HttpRequest.Query"/>）。</param>
    /// <param name="rawQueryString">原始查询串（含前导 <c>?</c>）；没有命中时原样返回它。</param>
    /// <param name="sensitiveKeys">要脱敏的参数名。</param>
    /// <returns>脱敏后的查询串；输入为空或没有敏感参数时返回 <paramref name="rawQueryString"/> 本身。</returns>
    public static string? Redact(IEnumerable<KeyValuePair<string, StringValues>> query, string? rawQueryString, IReadOnlyCollection<string> sensitiveKeys)
    {
        Check.NotNull(query);
        Check.NotNull(sensitiveKeys);

        if (string.IsNullOrEmpty(rawQueryString) || sensitiveKeys.Count == 0)
        {
            return rawQueryString;
        }

        var hit = false;
        foreach (var pair in query)
        {
            if (IsSensitive(pair.Key, sensitiveKeys))
            {
                hit = true;
                break;
            }
        }

        if (!hit)
        {
            return rawQueryString;
        }

        var builder = new StringBuilder("?");
        var first = true;
        foreach (var pair in query)
        {
            var sensitive = IsSensitive(pair.Key, sensitiveKeys);

            // 敏感参数不论原本有几个值，都只留一个掩码 —— 值的个数本身也是信息。
            var values = sensitive ? [RedactedValue] : pair.Value.ToArray();
            foreach (var value in values)
            {
                if (!first)
                {
                    builder.Append('&');
                }

                first = false;
                builder.Append(Uri.EscapeDataString(pair.Key)).Append('=');
                builder.Append(sensitive ? RedactedValue : Uri.EscapeDataString(value ?? string.Empty));
            }
        }

        return builder.ToString();
    }
}
