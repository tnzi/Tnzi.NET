namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// MCP 服务器目录 — MCP 客户端运行时的唯一服务器枚举入口。
/// 把两个配置来源合并为有效服务器列表：
/// <list type="bullet">
/// <item>部署配置 <c>AI:Mcp:Servers</c>（<c>McpOptions.Servers</c>）— 运维可控，允许 stdio；</item>
/// <item>数据库注册表 <c>McpServerRegistration</c>（admin 运行时录入）— 仅允许 HTTP 系 transport。</item>
/// </list>
/// 同名条目 DB 优先（admin 运行时配置覆盖部署配置）。
/// <c>AI:Mcp:Enabled</c> 是整个 MCP 客户端子系统的总开关：关闭时两个来源均不生效。
/// </summary>
public interface IMcpServerCatalog
{
    /// <summary>
    /// 获取当前有效的 MCP 服务器配置列表（部署配置 + 启用的 DB 注册条目合并，同名 DB 优先）。
    /// MCP 子系统未启用（AI:Mcp:Enabled=false）时返回空列表。
    /// DB 快照带短 TTL 缓存（30 秒），注册表 CRUD 会主动失效本地缓存。
    /// </summary>
    Task<IReadOnlyList<McpServerConfig>> GetEffectiveServersAsync(CancellationToken ct = default);

    /// <summary>
    /// 按名称查找有效服务器配置（大小写不敏感）。未找到或子系统未启用时返回 null。
    /// </summary>
    Task<McpServerConfig?> FindServerAsync(string serverName, CancellationToken ct = default);

    /// <summary>
    /// 失效 DB 快照缓存（所有租户）。注册表 CRUD 成功后调用，使变更立即生效。
    /// </summary>
    void Invalidate();
}
