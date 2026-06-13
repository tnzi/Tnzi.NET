using Tnzi.AI.Infrastructure.Mcp;

namespace Tnzi.AI.Tests;

/// <summary>
/// 测试用 IMcpServerCatalog stub — 直接由 IOptions&lt;AIOptions&gt;.Mcp.Servers 物化，
/// 复刻生产 catalog 的总开关语义（Enabled=false → 空列表），不接 DB 注册表。
/// 供 MCP provider 单测在不关心 DB 合并行为时复用。
/// </summary>
public sealed class OptionsBackedMcpServerCatalog : IMcpServerCatalog
{
    private readonly IOptions<AIOptions> _options;

    public OptionsBackedMcpServerCatalog(IOptions<AIOptions> options)
    {
        _options = options;
    }

    public Task<IReadOnlyList<McpServerConfig>> GetEffectiveServersAsync(CancellationToken ct = default)
    {
        var mcp = _options.Value.Mcp;
        if (mcp == null || !mcp.Enabled || mcp.Servers == null)
        {
            return Task.FromResult<IReadOnlyList<McpServerConfig>>(Array.Empty<McpServerConfig>());
        }

        return Task.FromResult<IReadOnlyList<McpServerConfig>>(
            mcp.Servers.Where(s => !string.IsNullOrWhiteSpace(s.Name)).ToList());
    }

    public async Task<McpServerConfig?> FindServerAsync(string serverName, CancellationToken ct = default)
    {
        var servers = await GetEffectiveServersAsync(ct);
        return servers.FirstOrDefault(s => string.Equals(s.Name, serverName, StringComparison.OrdinalIgnoreCase));
    }

    public void Invalidate()
    {
    }
}
