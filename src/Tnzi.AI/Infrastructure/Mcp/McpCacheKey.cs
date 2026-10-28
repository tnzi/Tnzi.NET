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
/// 同一份凭据，所有租户共用一条连接，键退化为服务器名。
/// </para>
/// <para>
/// 宿主级注册行（<c>TenantId</c> 为 null）不能也退化为服务器名：它可以与部署配置条目同名（DB 条目覆盖同名配置），
/// 而多租户下全局过滤器只让宿主上下文看见宿主行，租户上下文解析到的仍是部署配置那一条。两者共用一个键时，
/// 谁先建连谁赢：宿主先跑，租户的 agent 拿到的就是宿主的内部端点与宿主的令牌。故宿主行落在
/// <see cref="HostRegistryPartition"/> 分区，与部署配置分开。
/// </para>
/// </remarks>
internal static class McpCacheKey
{
    /// <summary>
    /// 宿主级注册行（<c>TenantId</c> 为 null）的分区名。不是 GUID 的形状，不会与任何租户键相撞。
    /// </summary>
    internal const string HostRegistryPartition = "host";

    /// <summary>
    /// 分隔符用单元分隔符（U+001F）：租户键是 GUID、服务器名是配置/注册表里的显示名，
    /// 两者都不会包含它，因此名字里塞分隔符也伪造不出另一个租户的键。
    /// </summary>
    private const char Separator = (char)0x1F;

    /// <summary>该配置在运行时缓存中的分区键。</summary>
    internal static string For(McpServerConfig config)
    {
        Check.NotNull(config);
        return config is UserMcpServerConfig user
            ? ForUser(user.UserId, user.Name)
            : For(config.TenantKey, config.Name ?? string.Empty);
    }

    /// <summary>
    /// 用户专属服务器的分区键：<c>user:{用户 id}</c> 段 + 服务器名。段头 <c>user:</c> 不是 GUID 的形状，
    /// 与租户键、<see cref="HostRegistryPartition"/> 都不会相撞；用户 id 全局唯一，不再叠租户段。
    /// </summary>
    internal static string ForUser(Guid userId, string serverName) =>
        UserSegment(userId) + Separator + serverName;

    /// <summary>该分区键是否属于给定用户（<paramref name="serverName"/> 为 null = 该用户的任何服务器）。</summary>
    internal static bool BelongsToUser(string cacheKey, Guid userId, string? serverName = null) =>
        cacheKey.StartsWith(UserSegment(userId) + Separator, StringComparison.Ordinal)
        && (serverName is null || MatchesServerName(cacheKey, serverName));

    private static string UserSegment(Guid userId) => "user:" + userId.ToString("N");

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
