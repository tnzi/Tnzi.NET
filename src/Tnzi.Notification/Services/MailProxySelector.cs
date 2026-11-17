using System.Net;
using MailKit.Net.Proxy;

namespace Tnzi.Notification.Services;

/// <summary>
/// 一条邮件连接（SMTP 发信、IMAP 收回执）该不该经代理、经哪个代理。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>补的是哪个洞。</b>容器化部署常常没有 DNS、也没有直达外网的路由，唯一的出口是标准环境变量
/// （<c>HTTPS_PROXY</c> / <c>HTTP_PROXY</c> / <c>NO_PROXY</c>）给的 HTTP 代理。<c>HttpClient</c> 自己会读这些变量，
/// MailKit 不会：它直接解析 SMTP 主机名，在这种宿主上每一封信都死在 <c>Dns.GetHostEntry</c>
/// （<c>SocketException (11): Resource temporarily unavailable</c>），包括密码重置与签署邀请。
/// </para>
/// <para>
/// <b>怎么选</b>（先到先得）：
/// <list type="number">
/// <item>这一节配置写了 <c>Proxy</c>：一律经它，<c>NO_PROXY</c> 不管。显式写的就是这条连接要的。</item>
/// <item><c>UseEnvironmentProxy = false</c>：直连（与没有代理时的行为一字不差）。</item>
/// <item>否则读环境变量，与 .NET 的 <c>HttpClient</c> 同一套变量、同一个大小写优先级（小写先）：
/// <c>https_proxy</c> → <c>all_proxy</c> → <c>http_proxy</c>。目标在 <c>NO_PROXY</c> 里、或是本机回环时直连。</item>
/// </list>
/// </para>
/// <para>
/// ★ <b>为什么 <c>HTTPS_PROXY</c> 优先</b>：经 HTTP 代理走 SMTP 用的是 <c>CONNECT</c> 隧道，与 HTTPS 请求同一个形状；
/// 只给 <c>HTTP_PROXY</c> 的部署多半两个变量指向同一个代理，所以它垫底而不是不认。
/// 大写 <c>HTTP_PROXY</c> 在 CGI 环境（设了 <c>REQUEST_METHOD</c>）下不读，原因同 .NET：那里它可能来自请求头（httpoxy）。
/// </para>
/// <para>
/// ★★ <b>TLS 仍然对真正的 SMTP 服务器握</b>：代理只负责把 TCP 连到 <c>主机:端口</c>，主机名交给代理去解析
/// （HTTP <c>CONNECT</c> 与 SOCKS5 都带主机名过去），MailKit 随后在隧道里按原主机名做 STARTTLS / 隐式 TLS，
/// 证书校验与直连时完全相同。代理看到的只有密文。
/// </para>
/// <para>
/// 环境变量里的值解析不了时当作没有（与 <c>HttpClient</c> 一致）；配置里的 <c>Proxy</c> 解析不了是配置错误，
/// 启动校验就拒绝（<see cref="Options.NotificationOptionsValidator"/>）。
/// </para>
/// </remarks>
internal static class MailProxySelector
{
    /// <summary>认得的代理协议。<c>socks5h</c> 与 <c>socks5</c> 同义：MailKit 都把主机名交给代理解析。</summary>
    internal static readonly IReadOnlyList<string> SupportedSchemes = ["http", "https", "socks4", "socks4a", "socks5", "socks5h"];

    private const int DefaultSocksPort = 1080;

    /// <summary>
    /// 连 <paramref name="host"/>:<paramref name="port"/> 该用的代理地址；<see langword="null"/> = 直连。
    /// </summary>
    /// <param name="configuredProxy">这一节配置里的 <c>Proxy</c>。</param>
    /// <param name="useEnvironmentProxy">这一节配置里的 <c>UseEnvironmentProxy</c>。</param>
    /// <param name="host">邮件服务器主机名或 IP。</param>
    /// <param name="port">邮件服务器端口（<c>NO_PROXY</c> 条目可以带端口）。</param>
    /// <param name="environment">读环境变量；测试传假的，生产传 <see cref="Environment.GetEnvironmentVariable(string)"/>。</param>
    /// <exception cref="InvalidOperationException">配置里的 <c>Proxy</c> 解析不了。</exception>
    internal static Uri? Select(
        string? configuredProxy, bool useEnvironmentProxy, string host, int port, Func<string, string?> environment)
    {
        if (!string.IsNullOrWhiteSpace(configuredProxy))
        {
            return TryParseProxy(configuredProxy, out var configured)
                ? configured
                : throw new InvalidOperationException(
                    $"The mail proxy '{Redact(configuredProxy)}' is not a valid proxy address; expected {DescribeSupportedForm()}.");
        }

        if (!useEnvironmentProxy)
            return null;

        var fromEnvironment = ReadEnvironmentProxy(environment);
        if (fromEnvironment == null)
            return null;

        if (IsLoopback(host) || IsBypassed(host, port, FirstSet(environment, "no_proxy", "NO_PROXY")))
            return null;

        return fromEnvironment;
    }

    /// <summary>
    /// 把一个代理地址变成 MailKit 的代理客户端。凭据取自地址里的 <c>user:password@</c>（按 URL 编码解码）。
    /// </summary>
    internal static IProxyClient CreateClient(Uri proxy)
    {
        var credentials = CredentialsOf(proxy);
        var host = proxy.IdnHost;
        var port = proxy.Port < 0 ? DefaultSocksPort : proxy.Port;

        return proxy.Scheme switch
        {
            "http" => credentials == null ? new HttpProxyClient(host, port) : new HttpProxyClient(host, port, credentials),
            "https" => credentials == null ? new HttpsProxyClient(host, port) : new HttpsProxyClient(host, port, credentials),
            "socks4" => credentials == null ? new Socks4Client(host, port) : new Socks4Client(host, port, credentials),
            "socks4a" => credentials == null ? new Socks4aClient(host, port) : new Socks4aClient(host, port, credentials),
            "socks5" or "socks5h" => credentials == null ? new Socks5Client(host, port) : new Socks5Client(host, port, credentials),
            _ => throw new InvalidOperationException($"Unsupported mail proxy scheme '{proxy.Scheme}'."),
        };
    }

    /// <summary>
    /// 给一个即将连接的 MailKit 客户端（<c>SmtpClient</c> / <c>ImapClient</c>）装上代理；该直连时什么也不做。
    /// 返回装上的代理地址（已去掉凭据，可以写日志），直连返回 <see langword="null"/>。
    /// </summary>
    internal static string? Apply(
        MailKit.MailService client, string? configuredProxy, bool useEnvironmentProxy, string host, int port,
        Func<string, string?>? environment = null)
    {
        var proxy = Select(configuredProxy, useEnvironmentProxy, host, port, environment ?? Environment.GetEnvironmentVariable);
        if (proxy == null)
            return null;

        client.ProxyClient = CreateClient(proxy);
        return $"{proxy.Scheme}://{proxy.Authority}";
    }

    /// <summary>
    /// 解析一个代理地址。不带协议的（<c>proxy:3128</c>）按 <c>http</c>，与 <c>HttpClient</c> 一致。
    /// </summary>
    internal static bool TryParseProxy(string? value, out Uri proxy)
    {
        proxy = null!;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "http://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed)
            || string.IsNullOrEmpty(parsed.Host)
            || !SupportedSchemes.Contains(parsed.Scheme, StringComparer.Ordinal))
            return false;

        proxy = parsed;
        return true;
    }

    /// <summary>
    /// 目标是否在 <c>NO_PROXY</c> 里。条目以逗号分隔：
    /// <c>*</c> = 全部直连；<c>example.com</c> / <c>.example.com</c> / <c>*.example.com</c> = 这个域与它的子域；
    /// IP 字面量按地址比；<c>10.0.0.0/8</c> 这类 CIDR 按网段比；条目带 <c>:端口</c> 时端口也要一致。
    /// </summary>
    internal static bool IsBypassed(string host, int port, string? noProxy)
    {
        if (string.IsNullOrWhiteSpace(noProxy))
            return false;

        var target = NormalizeHost(host);
        var targetAddress = IPAddress.TryParse(target, out var address) ? address : null;

        foreach (var raw in noProxy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw == "*")
                return true;

            if (raw.Contains('/'))
            {
                if (targetAddress != null && IPNetwork.TryParse(raw, out var network) && network.Contains(targetAddress))
                    return true;
                continue;
            }

            var (entryHost, entryPort) = SplitPort(raw);
            if (entryPort != null && entryPort != port)
                continue;

            if (IPAddress.TryParse(entryHost, out var entryAddress))
            {
                if (targetAddress != null && entryAddress.Equals(targetAddress))
                    return true;
                continue;
            }

            var domain = entryHost.StartsWith("*.", StringComparison.Ordinal) ? entryHost[2..] : entryHost.TrimStart('.');
            domain = domain.TrimEnd('.');
            if (domain.Length == 0)
                continue;

            if (target.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || target.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>本机回环（<c>localhost</c>、<c>*.localhost</c>、<c>127.0.0.0/8</c>、<c>::1</c>）从不经环境代理：代理那头的「本机」不是这台。</summary>
    internal static bool IsLoopback(string host)
    {
        var target = NormalizeHost(host);
        if (target.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || target.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        return IPAddress.TryParse(target, out var address) && IPAddress.IsLoopback(address);
    }

    /// <summary>去掉凭据后的代理地址，用在日志与错误信息里。</summary>
    internal static string Redact(string proxy)
    {
        var at = proxy.LastIndexOf('@');
        if (at < 0)
            return proxy;

        var schemeEnd = proxy.IndexOf("://", StringComparison.Ordinal);
        var prefix = schemeEnd >= 0 && schemeEnd < at ? proxy[..(schemeEnd + 3)] : string.Empty;
        return prefix + "***@" + proxy[(at + 1)..];
    }

    internal static string DescribeSupportedForm()
        => "[scheme://][user:password@]host[:port] with scheme " + string.Join(", ", SupportedSchemes);

    private static Uri? ReadEnvironmentProxy(Func<string, string?> environment)
    {
        // ★ 大写 HTTP_PROXY 在 CGI 下不读：那里它可能是客户端用 Proxy: 请求头注入的（httpoxy），.NET 同此。
        var isCgi = !string.IsNullOrEmpty(environment("REQUEST_METHOD"));
        string?[] candidates =
        [
            environment("https_proxy"), environment("HTTPS_PROXY"),
            environment("all_proxy"), environment("ALL_PROXY"),
            environment("http_proxy"), isCgi ? null : environment("HTTP_PROXY"),
        ];

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            // 一个变量写坏了就看下一个，最终都不行就直连：与 HttpClient 一样不因环境变量拒绝发信。
            if (TryParseProxy(candidate, out var proxy))
                return proxy;
        }

        return null;
    }

    private static string? FirstSet(Func<string, string?> environment, params string[] names)
        => names.Select(environment).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static NetworkCredential? CredentialsOf(Uri proxy)
    {
        if (string.IsNullOrEmpty(proxy.UserInfo))
            return null;

        var colon = proxy.UserInfo.IndexOf(':');
        var user = Uri.UnescapeDataString(colon < 0 ? proxy.UserInfo : proxy.UserInfo[..colon]);
        var password = colon < 0 ? string.Empty : Uri.UnescapeDataString(proxy.UserInfo[(colon + 1)..]);
        return new NetworkCredential(user, password);
    }

    private static string NormalizeHost(string host)
    {
        var trimmed = host.Trim().TrimEnd('.');
        return trimmed.StartsWith('[') && trimmed.EndsWith(']') ? trimmed[1..^1] : trimmed;
    }

    /// <summary>拆开 <c>NO_PROXY</c> 条目里的端口：<c>host:25</c>、<c>[::1]:25</c>；裸 IPv6 不当端口拆。</summary>
    private static (string Host, int? Port) SplitPort(string entry)
    {
        if (entry.StartsWith('['))
        {
            var close = entry.IndexOf(']');
            if (close < 0)
                return (entry, null);

            var inner = entry[1..close];
            return entry.Length > close + 2 && entry[close + 1] == ':' && int.TryParse(entry[(close + 2)..], out var bracketPort)
                ? (inner, bracketPort)
                : (inner, null);
        }

        var colon = entry.IndexOf(':');
        if (colon < 0 || colon != entry.LastIndexOf(':'))
            return (entry, null);

        return int.TryParse(entry[(colon + 1)..], out var p) ? (entry[..colon], p) : (entry, null);
    }
}
