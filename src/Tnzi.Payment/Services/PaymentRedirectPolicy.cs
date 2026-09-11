namespace Tnzi.Payment.Services;

/// <summary>
/// 付款人回跳地址的准入判定：调用方指定的 <c>ReturnUrl</c> / <c>CancelUrl</c> 会被原样交给
/// 支付渠道，作为付款完成后把**付款人的浏览器**送去的地方。
/// </summary>
/// <remarks>
/// <para>
/// 不校验就是一个开放重定向，而且是最贵的那一种：跳转发生在渠道的支付页之后，
/// 付款人刚刚输完卡号，此刻落在一个仿冒的「订单完成」页上，不会有任何怀疑。
/// 链接本身来自本商户的域名与本商户的下单接口，邮件网关与浏览器都挑不出毛病。
/// </para>
/// <para>
/// ★ <b>未配置时失败关闭</b>。放行是不行的 —— 那正是现状，而「没人配」恰恰是默认状态；
/// 安静地改用默认回跳地址也不行 —— 付款人会落在一个调用方没有要求的页面上，
/// 而调用方拿到的是 200。所以是 400，外加一条指名要配哪个配置项的日志：
/// 部署疏漏与「用户填错了」在客户端看起来完全一样，服务端必须把话说全。
/// </para>
/// <para>
/// 主机名<b>精确匹配</b>（大小写不敏感），不支持通配符子域：<c>*.example.com</c> 这样的模式
/// 会把一个被接管的子域（常见于过期的 CNAME）一起放进来，而多写几行主机名的成本是零。
/// </para>
/// </remarks>
public static class PaymentRedirectPolicy
{
    /// <summary>
    /// 判断一个调用方指定的回跳地址是否可用。
    /// </summary>
    /// <param name="url">调用方给的地址；null / 空白表示「没指定」，一律放行（由服务端回退到默认值）。</param>
    /// <param name="options">支付配置：允许的主机名清单 + 默认回跳地址。</param>
    /// <returns>可用返回 true。</returns>
    public static bool IsAllowed(string? url, PaymentOptions options)
    {
        Check.NotNull(options);

        if (string.IsNullOrWhiteSpace(url))
            return true;

        // 必须是绝对的 http/https 地址：这个值最终交给渠道，相对路径在渠道那一侧没有意义，
        // 而 javascript: / data: 这类 scheme 在浏览器里就是脚本执行。
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        return AllowedHosts(options).Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 允许的主机名集合：显式配置的清单，加上默认回跳地址所在的主机。
    /// </summary>
    /// <remarks>
    /// 把默认回跳地址的主机自动算进来，是因为它已经是这台部署公开承认的落地页 ——
    /// 逼运维把同一个主机名在两处各写一遍，只会换来一次配漏。
    /// </remarks>
    public static IReadOnlyCollection<string> AllowedHosts(PaymentOptions options)
    {
        Check.NotNull(options);

        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var host in options.AllowedRedirectHosts)
        {
            if (!string.IsNullOrWhiteSpace(host))
                hosts.Add(host.Trim());
        }

        if (!string.IsNullOrWhiteSpace(options.DefaultReturnUrl)
            && Uri.TryCreate(options.DefaultReturnUrl, UriKind.Absolute, out var defaultUri))
        {
            hosts.Add(defaultUri.Host);
        }

        return hosts;
    }
}
