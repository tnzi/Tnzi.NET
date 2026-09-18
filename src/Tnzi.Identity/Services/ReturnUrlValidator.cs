namespace Tnzi.Identity.Services;

/// <summary>
/// 判定一个调用方给的 <c>returnUrl</c> 能不能作为跳转目标。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <strong>存在的理由：OAuth 回调页会把令牌放进目标 URL 的 fragment 再跳过去。</strong>
/// 回调页在没有 opener 的情况下（用户直接点开一条链接就是这种情况）执行的是
/// <c>window.location.href = new URL(returnUrl, origin) + "#accessToken=…&amp;refreshToken=…"</c>，
/// 而 <c>new URL("https://evil.example", origin)</c> 解析出来是那个绝对地址 —— 基地址不起作用。
/// 于是一条 <c>/auth/oauth/google/login?returnUrl=https://evil.example</c> 就足以把受害者的
/// 访问令牌与刷新令牌送到别人手上：受害者在第三方那边通常已经授权过，整个流程静默完成。
/// </para>
/// <para>
/// 同一个参数在 <c>confirm-email</c> 上直接进 <c>Redirect()</c>，那是它平常的那一半 —— 开放重定向。
/// </para>
/// <para>
/// ★ <strong>判据只有两条，刻意不做「聪明」的归一化</strong>：相对路径必须是单个 <c>/</c> 开头的
/// 站内路径；绝对地址的<b>源</b>（scheme + host + port）必须在白名单里。任何看不懂的一律拒绝。
/// 试图「修好」一个可疑输入（去掉多余斜杠、补上协议）是这类校验器最常见的翻车方式：
/// 修完之后放行的那个东西，和调用方写下的那个已经不是一回事了。
/// </para>
/// </remarks>
public static class ReturnUrlValidator
{
    /// <summary>
    /// <paramref name="returnUrl"/> 是否允许作为跳转目标。
    /// </summary>
    /// <param name="returnUrl">调用方给的目标地址；<c>null</c> 或空白视为「没给」，返回 <c>true</c>。</param>
    /// <param name="allowedOrigins">
    /// 允许的绝对地址来源（如 <c>https://app.example.com</c>）。为空时<b>只放行相对路径</b> ——
    /// 没配白名单不等于放行一切，那正是本类要消灭的默认值。
    /// </param>
    public static bool IsAllowed(string? returnUrl, IReadOnlyCollection<string>? allowedOrigins)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return true;
        }

        // 控制字符与空白：正常的 URL 里它们要么被百分号编码、要么不该出现。
        // 留着它们会让「浏览器怎么解析这一串」与「这里怎么解析这一串」出现分歧，
        // 而所有绕过都住在那个分歧里。
        foreach (var ch in returnUrl)
        {
            if (char.IsControl(ch) || char.IsWhiteSpace(ch))
            {
                return false;
            }
        }

        // 反斜杠一律拒绝。浏览器在若干位置把 `\` 当 `/` 处理，于是 `/\evil.example`
        // 与 `\\evil.example` 都可能被解析成协议相对地址跳出站外，而按字符串看它们「以 / 开头」。
        if (returnUrl.Contains('\\'))
        {
            return false;
        }

        if (returnUrl.StartsWith('/'))
        {
            // `//host` 是协议相对地址，不是站内路径。
            return !returnUrl.StartsWith("//", StringComparison.Ordinal);
        }

        if (allowedOrigins is not { Count: > 0 })
        {
            return false;
        }

        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var target))
        {
            return false;
        }

        // 只认 http/https。javascript: 与 data: 在 `location.href` 上是可执行的。
        if (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        foreach (var origin in allowedOrigins)
        {
            if (string.IsNullOrWhiteSpace(origin))
            {
                continue;
            }

            if (Uri.TryCreate(origin.Trim(), UriKind.Absolute, out var allowed)
                && string.Equals(allowed.Scheme, target.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(allowed.Host, target.Host, StringComparison.OrdinalIgnoreCase)
                && allowed.Port == target.Port)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 合并两处来源，得到实际生效的白名单：<c>Identity:OAuth:AllowedReturnOrigins</c>
    /// 优先，未配置时回退到前端 origin（<c>System:FrontendUrl</c>，经 <c>FrontendUrlResolver</c>）。
    /// </summary>
    /// <remarks>
    /// 回退到前端 origin 是为了让绝大多数部署<b>不必额外配一项</b>就得到正确行为：
    /// 那个键本来就指着前端所在的源，而前端正是 OAuth 唯一要回到的地方。
    /// </remarks>
    public static IReadOnlyCollection<string> ResolveAllowedOrigins(
        IReadOnlyCollection<string>? configured, string? frontendUrl)
    {
        if (configured is { Count: > 0 })
        {
            return configured;
        }

        return string.IsNullOrWhiteSpace(frontendUrl) ? [] : [frontendUrl];
    }
}
