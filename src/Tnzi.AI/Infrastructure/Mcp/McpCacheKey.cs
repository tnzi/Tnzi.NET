namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// MCP 运行时缓存（连接缓存 / 工具缓存）的分区键。
/// </summary>
/// <remarks>
/// <para>
/// 多租户下 <c>McpServerRegistration</c> 的唯一索引是 (TenantId, Name)：两个租户可以注册同名 server
/// 而端点与凭据各不相同。只按 Name 分桶时，先跑的租户建立的连接（携带其凭据）会被后来的租户命中 ——
/// <c>IMcpServerCatalog</c> 已经按租户分桶下发了正确的配置，缓存却把它盖掉了。故键必须携带租户维度。
/// </para>
/// <para>
/// 部署配置（<c>AI:Mcp:Servers</c>）条目的 <see cref="McpServerConfig.TenantKey"/> 为 null：那是进程级的
/// 同一份凭据，所有租户共用一条连接，键退化为服务器名 —— 单租户部署与引入租户维度之前逐字相同。
/// </para>
/// </remarks>
internal static class McpCacheKey
{
    /// <summary>
    /// 分隔符用单元分隔符（U+001F）：租户键是 GUID、服务器名是配置/注册表里的显示名，
    /// 两者都不会包含它，因此名字里塞分隔符也伪造不出另一个租户的键。
    /// </summary>
    private const char Separator = (char)0x1F;

    /// <summary>该配置在运行时缓存中的分区键。</summary>
    internal static string For(McpServerConfig config)
    {
        Check.NotNull(config);
        return For(config.TenantKey, config.Name ?? string.Empty);
    }

    /// <summary>按租户键与服务器名组装分区键；租户键为空时退化为服务器名。</summary>
    internal static string For(string? tenantKey, string serverName) =>
        string.IsNullOrEmpty(tenantKey) ? serverName : tenantKey + Separator + serverName;

    /// <summary>取分区键的服务器名部分（用于日志与按名失效）。</summary>
    internal static string ServerName(string cacheKey)
    {
        var separatorIndex = cacheKey.LastIndexOf(Separator);
        return separatorIndex >= 0 ? cacheKey[(separatorIndex + 1)..] : cacheKey;
    }

    /// <summary>分区键的服务器名部分是否等于给定名称（忽略大小写）。</summary>
    internal static bool MatchesServerName(string cacheKey, string serverName) =>
        string.Equals(ServerName(cacheKey), serverName, StringComparison.OrdinalIgnoreCase);
}
