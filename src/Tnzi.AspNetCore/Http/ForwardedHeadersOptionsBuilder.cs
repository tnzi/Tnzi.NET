namespace Tnzi.AspNetCore.Http;

/// <summary>
/// 把部署侧的受信代理声明（<see cref="TrustedProxyOptions"/>）翻译成
/// <see cref="ForwardedHeadersOptions"/>。
/// </summary>
/// <remarks>
/// <para>
/// 独立成类只有一个理由：<b>它是可以单独跑一遍的那一半</b>。
/// 「配置写对了没有」与「中间件按配置做对了没有」是两个问题，
/// 混在应用启动里就只能靠起一整个站点来回答。
/// </para>
/// </remarks>
public static class ForwardedHeadersOptionsBuilder
{
    /// <summary>
    /// 按配置构建转发头选项。
    /// </summary>
    /// <param name="options">AspNetCore 模块配置。</param>
    /// <returns>可直接交给 <c>UseForwardedHeaders</c> 的选项。</returns>
    /// <exception cref="TnziException">受信代理地址或网段写法非法。</exception>
    public static ForwardedHeadersOptions Build(AspNetCoreOptions options)
    {
        Check.NotNull(options);

        var trusted = options.TrustedProxies ?? new TrustedProxyOptions();

        var built = new ForwardedHeadersOptions
        {
            // X-Forwarded-Prefix 与其余三个头是同一批：受信代理给的前缀写进 Request.PathBase，
            // 未受信的连接一律忽略。它曾经绕过这道判定被单独采信（见 AspNetCoreModule 的中间件注释）。
            ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
                               Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto |
                               Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost |
                               Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedPrefix,
            ForwardLimit = trusted.ForwardLimit
        };

        if (!string.IsNullOrWhiteSpace(trusted.ForwardedForHeaderName))
        {
            built.ForwardedForHeaderName = trusted.ForwardedForHeaderName;
        }

        // 顺序要紧：先清空出厂默认（loopback），再追加声明的地址。
        // 反过来会把刚加进去的一起清掉，而外观上配置是「生效」的。
        if (trusted.TrustAllProxies)
        {
            built.KnownProxies.Clear();
            built.KnownIPNetworks.Clear();
        }

        foreach (var proxy in trusted.KnownProxies ?? [])
        {
            if (string.IsNullOrWhiteSpace(proxy))
            {
                continue;
            }

            if (!IPAddress.TryParse(proxy.Trim(), out var address))
            {
                throw new TnziException(
                    $"AspNetCore:TrustedProxies:KnownProxies contains '{proxy}', which is not a valid IP address.");
            }

            built.KnownProxies.Add(address);
        }

        foreach (var network in trusted.KnownNetworks ?? [])
        {
            if (string.IsNullOrWhiteSpace(network))
            {
                continue;
            }

            built.KnownIPNetworks.Add(ParseNetwork(network.Trim()));
        }

        return built;
    }

    /// <summary>
    /// 解析 CIDR 网段。
    /// </summary>
    /// <remarks>
    /// 写错的网段<b>必须抛</b>而不是跳过：跳过的后果是那一段代理不再受信，
    /// 于是所有调用方共用代理的地址 —— 限流把所有人算在一个桶里，
    /// 看起来像「限流太严」而不像「配置写错了」。
    /// </remarks>
    public static IPNetwork ParseNetwork(string cidr)
    {
        Check.NotNullOrWhiteSpace(cidr);

        if (!IPNetwork.TryParse(cidr, out var parsed))
        {
            throw new TnziException(
                $"AspNetCore:TrustedProxies:KnownNetworks contains '{cidr}', "
                + "which is not valid CIDR notation (e.g. '10.0.0.0/8').");
        }

        return parsed;
    }
}
