namespace Tnzi.Notification.Services.Internal;

/// <summary>
/// 把一次投递的退订链接组装成 RFC 8058 的两个信头。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>为什么是信头而不是正文里的链接。</b>本模块把一条消息<b>只渲染一次</b>，多个收件人共用同一份
/// <see cref="Message.Content"/> —— 这是「一次群发只渲染一次」这个设计的直接后果。而退订令牌是
/// <b>按地址</b>签发的，所以正文里放不进一条对每个收件人都正确的链接。信头恰好相反：它在投递那一刻
/// 逐封写入，天然是按收件人的。<c>_DefaultEmail.cshtml</c> 里那个 <c>UnsubscribeUrl</c> 插槽因此只对
/// 单收件人的消息填得对，框架不去填它。
/// </para>
/// <para>
/// ★ <b>只给非事务性消息写。</b>在密码重置上写「退订」不只是没意义 —— 客户端会照样渲染那个按钮，
/// 于是收件人可以「退订」掉自己的安全邮件，然后再也收不到验证码。豁免判据与退订、偏好、
/// 每小时上限逐字一致（<see cref="Message.IsTransactional"/>）。
/// </para>
/// <para>
/// ★ <b>没配落地页就什么都不写。</b>猜一个主机名或写相对地址，得到的是一个看着能用、点下去到不了的
/// 退订链接 —— 比没有退订更糟。
/// </para>
/// </remarks>
internal static class UnsubscribeHeaders
{
    internal const string ListUnsubscribe = "List-Unsubscribe";
    internal const string ListUnsubscribePost = "List-Unsubscribe-Post";

    /// <summary>RFC 8058 规定的固定取值，邮件服务商据此判断可以一键 POST。</summary>
    internal const string OneClickValue = "List-Unsubscribe=One-Click";

    /// <summary>
    /// 组装信头。返回 <see langword="null"/> 表示这封信不该带退订信头。
    /// </summary>
    /// <param name="isTransactional">这条消息是否为事务性（事务性一律不带）。</param>
    /// <param name="landingUrl">落地页绝对地址；为空则不带。</param>
    /// <param name="oneClickEndpoint">一键退订 POST 端点绝对地址；为空则只写落地页链接。</param>
    /// <param name="token">本次投递的退订令牌。</param>
    internal static Dictionary<string, string>? Build(
        bool isTransactional, string? landingUrl, string? oneClickEndpoint, string token)
    {
        if (isTransactional || string.IsNullOrWhiteSpace(landingUrl) || string.IsNullOrWhiteSpace(token))
            return null;

        var encoded = Uri.EscapeDataString(token);
        var landing = AppendToken(landingUrl, encoded);

        // ★ 顺序有意义：RFC 8058 要求一键 POST 的那个 URI 排在最前，客户端从左往右挑第一个能用的。
        var uris = string.IsNullOrWhiteSpace(oneClickEndpoint)
            ? $"<{landing}>"
            : $"<{AppendToken(oneClickEndpoint, encoded)}>, <{landing}>";

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ListUnsubscribe] = uris,
        };

        // ★ 只有真的给出了 POST 端点才声明一键能力：声明了却没有可 POST 的地址，
        // 邮件服务商会 POST 到落地页那个 GET 端点上，收件人点了「退订」而什么也没发生。
        if (!string.IsNullOrWhiteSpace(oneClickEndpoint))
            headers[ListUnsubscribePost] = OneClickValue;

        return headers;
    }

    /// <summary>把令牌接到 URL 的查询串上，已有查询参数时用 <c>&amp;</c> 续接。</summary>
    private static string AppendToken(string url, string encodedToken)
    {
        var trimmed = url.Trim();
        var separator = trimmed.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{trimmed}{separator}token={encodedToken}";
    }
}
