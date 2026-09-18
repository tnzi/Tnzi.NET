namespace Tnzi.AspNetCore.Middleware;

/// <summary>
/// 请求体 / 响应体日志采集的闸门：按 Content-Type 决定采不采，超过上界只留标记。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由是两个开关都能在配置中心热开，而此前采集<b>没有任何上界</b>：请求体 <c>ReadToEndAsync()</c>
/// 成 string 再截断、响应体先整块缓冲进 <c>MemoryStream</c> 再截断 —— <c>MaxRequestBodyLength</c> /
/// <c>MaxResponseBodyLength</c> 裁剪的是<b>已经在内存里</b>的那个 string，对内存峰值毫无约束。
/// 打开开关后一次 100 MB 的上传或下载就是几百 MB 的临时对象，几个并发即 OOM；
/// 而响应体的整块缓冲还把 SSE / 分块流式端点变成「等全部生成完再一次性吐出」。
/// </para>
/// <para>
/// 两条规则：①只采文本型体（JSON / XML / text / 表单编码），文件上传、二进制、<c>text/event-stream</c>
/// 一律不采 —— 它们既不可脱敏也没有排障价值；②超过 <see cref="RequestTrackingOptions.MaxCapturedBodyBytes"/>
/// 的体<b>不记</b>而不是记一半：截断过的 JSON 是非法串，脱敏器只能原样返回，「记一半」等于把凭据记进去。
/// </para>
/// </remarks>
internal static class BodyCapturePolicy
{
    /// <summary>超过采集上界时写进日志的标记。</summary>
    public const string RequestExceededMarker = "[request body exceeded capture limit]";

    /// <inheritdoc cref="RequestExceededMarker"/>
    public const string ResponseExceededMarker = "[response body exceeded capture limit]";

    /// <summary>Content-Type 不在采集范围内时写进日志的标记。</summary>
    public static string RequestNotCapturedMarker(string? contentType)
        => $"[request body not captured: {Describe(contentType)}]";

    /// <inheritdoc cref="RequestNotCapturedMarker"/>
    public static string ResponseNotCapturedMarker(string? contentType, string? contentEncoding = null)
        => IsEncoded(contentEncoding)
            ? $"[response body not captured: {Describe(contentType)}; content-encoding {contentEncoding!.Trim()}]"
            : $"[response body not captured: {Describe(contentType)}]";

    /// <summary>
    /// 这个 Content-Encoding 表示体已被压缩 / 变换（空或 <c>identity</c> 视为未编码）。编码过的体采了只能是乱码，
    /// 脱敏对它无效，所以与不可采的 Content-Type 同等对待。
    /// </summary>
    public static bool IsEncoded(string? contentEncoding)
    {
        if (string.IsNullOrWhiteSpace(contentEncoding))
        {
            return false;
        }

        return !contentEncoding.Trim().Equals("identity", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 这个 Content-Type 的体值不值得读进日志。
    /// </summary>
    /// <remarks>
    /// 没有 Content-Type 按「可采」处理：上界仍然兜着内存，而一段没标类型的小体多半是排障时最想看的那种。
    /// <c>text/event-stream</c> 虽然是 text/*，但它是一条长连接上的无尽流，采了也只能是标记。
    /// </remarks>
    public static bool IsCapturable(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return true;
        }

        var separator = contentType.IndexOf(';');
        var mediaType = (separator >= 0 ? contentType[..separator] : contentType).Trim();

        if (mediaType.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (mediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // application/json、application/xml 及其 +json / +xml 变体（problem+json、hal+json、soap+xml……）
        return mediaType.EndsWith("/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("/xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase);
    }

    private static string Describe(string? contentType)
        => string.IsNullOrWhiteSpace(contentType) ? "unknown content type" : contentType.Trim();
}
