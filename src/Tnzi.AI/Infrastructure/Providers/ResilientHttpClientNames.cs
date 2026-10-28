namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// Names of the Polly-wrapped HttpClients registered by <see cref="AIModule"/>.
/// Each provider configured under AI:Providers gets its own named client so that
/// circuit-breaker state is isolated per provider (a 429 on one provider cannot
/// trip the circuit of another).
/// </summary>
/// <remarks>
/// <see cref="For"/> only hands out a dedicated name that <see cref="Register"/> has seen.
/// <c>IHttpClientFactory.CreateClient</c> returns a bare client for any name nobody registered,
/// with no exception and no log line, so a provider that exists only as a database row
/// (or overrides a config entry that is disabled) used to get no HTTP-level retry, circuit breaker
/// or timeout at all while the registration comment promised it the shared Fallback pipeline.
/// </remarks>
public static class ResilientHttpClientNames
{
    public const string Fallback = "Tnzi.AI.Resilient";

    /// <summary>
    /// 租户行自填端点（<c>Provider.Scope=Tenant</c> 且 <c>Endpoint</c> 非空）专用的客户端：与 <see cref="Fallback"/> 同一套弹性管线，
    /// 另外不跟随重定向、不走系统代理、每条 TCP 连接都过 SSRF egress 检查。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 租户端点在写入时已按 <c>EgressGuard</c> 检查过，但那只是那一刻的 DNS 结果：事后把域名改指内网，
    /// 或者端点回一个 307 指向内网地址，服务器就带着请求体与密钥连过去；守卫之前登记的存量行也从未被检查过。
    /// 刻意不落在 <c>{Fallback}:{providerName}</c> 的命名空间里，免得与某个配置条目的专属客户端同名。
    /// </para>
    /// <para>
    /// ★ 不走代理（<c>EgressGuardedConnector.CreateHandler</c> 设 <c>UseProxy = false</c>）：经代理时连接的是代理，
    /// 目标主机由代理去解析，连接期检查看不到它。配了 <c>HTTP_PROXY</c> / <c>HTTPS_PROXY</c> 的部署，
    /// 这个客户端仍然直连；只能经代理出网的环境里它连不出去，SSRF 防护要放在代理那一侧。
    /// </para>
    /// </remarks>
    public const string TenantEndpoint = "Tnzi.AI.TenantEndpoint";

    private static readonly ConcurrentDictionary<string, byte> RegisteredNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records that <see cref="AIModule"/> registered a dedicated resilient client for this provider.</summary>
    public static void Register(string providerName)
    {
        Check.NotNullOrWhiteSpace(providerName);
        RegisteredNames[providerName] = 0;
    }

    /// <summary>Dedicated client name when one was registered for the provider, otherwise the shared Fallback pipeline.</summary>
    public static string For(string? providerName) =>
        string.IsNullOrWhiteSpace(providerName) || !RegisteredNames.ContainsKey(providerName)
            ? Fallback
            : $"{Fallback}:{providerName}";
}
