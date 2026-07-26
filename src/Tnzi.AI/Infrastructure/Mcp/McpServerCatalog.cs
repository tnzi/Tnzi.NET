namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// IMcpServerCatalog 默认实现 - 合并部署配置（AI:Mcp:Servers）与数据库注册表
/// （McpServerRegistration，仅 IsEnabled 条目）为有效服务器列表。
/// </summary>
/// <remarks>
/// <para>
/// 合并语义：DB 条目优先（admin 运行时配置覆盖部署配置）。最终顺序 =
/// DB 条目（按 Priority 降序）在前 + 未被同名覆盖的部署配置条目（保持原顺序）在后 -
/// McpToolProvider 的同名工具去重保留先出现者，因此该顺序使 DB 高优先级服务器的工具胜出。
/// </para>
/// <para>
/// 缓存取舍：DB 快照按租户键缓存，30 秒 TTL。选择短 TTL 作为正确性基线
/// （无跨节点状态、多实例部署下 staleness 有界）；注册表 CRUD 额外调用
/// <see cref="Invalidate"/> 使本节点立即生效（连接缓存/工具缓存没有配置感知的 TTL，
/// CRUD 侧失效是必需的，见 McpServerRegistryService）。
/// </para>
/// <para>
/// 信任边界：DB 条目只物化为 HTTP 连接（mapper 拒绝 stdio）；legacy stdio 行被跳过并告警。
/// 凭证解密失败的条目按 fail-closed 跳过（不让一条坏行拖垮其他服务器）。
/// </para>
/// </remarks>
public class McpServerCatalog : IMcpServerCatalog
{
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromSeconds(30);

    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ILogger<McpServerCatalog> _logger;

    // DB 快照缓存（key = tenant id 或空串）。多租户开启时全局查询过滤器按当前租户过滤，
    // 因此快照必须按租户分桶，避免跨租户串数据。
    private readonly ConcurrentDictionary<string, Snapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly KeyedAsyncLock _snapshotLock = new();

    private sealed record Snapshot(IReadOnlyList<McpServerConfig> Servers, DateTimeOffset ExpiresAt);

    public McpServerCatalog(
        IOptionsMonitor<AIOptions> options,
        IServiceScopeFactory scopeFactory,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<McpServerCatalog> logger)
    {
        _options = Check.NotNull(options);
        _scopeFactory = Check.NotNull(scopeFactory);
        _dataProtectionProvider = Check.NotNull(dataProtectionProvider);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<McpServerConfig>> GetEffectiveServersAsync(CancellationToken ct = default)
    {
        var mcp = _options.CurrentValue.Mcp;
        if (mcp == null || !mcp.Enabled)
        {
            return Array.Empty<McpServerConfig>();
        }

        var configServers = (mcp.Servers ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .ToList();
        var dbServers = await GetDbServersAsync(ct).ConfigureAwait(false);

        if (dbServers.Count == 0)
        {
            return configServers;
        }

        // DB 条目在前（已按 Priority 降序），同名部署配置条目被覆盖（剔除）
        var dbNames = new HashSet<string>(dbServers.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        var merged = new List<McpServerConfig>(dbServers.Count + configServers.Count);
        merged.AddRange(dbServers);
        foreach (var server in configServers)
        {
            if (dbNames.Contains(server.Name))
            {
                _logger.LogDebug(
                    "MCP server '{ServerName}' from deployment configuration is overridden by a database registration",
                    server.Name);
                continue;
            }
            merged.Add(server);
        }

        return merged;
    }

    /// <inheritdoc />
    public async Task<McpServerConfig?> FindServerAsync(string serverName, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(serverName);
        var servers = await GetEffectiveServersAsync(ct).ConfigureAwait(false);
        return servers.FirstOrDefault(s => string.Equals(s.Name, serverName, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        _snapshots.Clear();
        _logger.LogDebug("Invalidated MCP server registry snapshot cache");
    }

    private async Task<IReadOnlyList<McpServerConfig>> GetDbServersAsync(CancellationToken ct)
    {
        // Scope 兼顾两件事：解析 Scoped 仓储 + 取当前租户键（ICurrentTenant 为 Scoped、AsyncLocal 流动）
        using var scope = _scopeFactory.CreateScope();
        var tenantKey = scope.ServiceProvider.GetService<ICurrentTenant>()?.Id?.ToString() ?? "__default";

        if (_snapshots.TryGetValue(tenantKey, out var snapshot) && snapshot.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return snapshot.Servers;
        }

        await using (await _snapshotLock.LockAsync(tenantKey, ct).ConfigureAwait(false))
        {
            if (_snapshots.TryGetValue(tenantKey, out snapshot) && snapshot.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return snapshot.Servers;
            }

            var servers = await LoadDbServersAsync(scope.ServiceProvider, ct).ConfigureAwait(false);
            _snapshots[tenantKey] = new Snapshot(servers, DateTimeOffset.UtcNow.Add(SnapshotTtl));
            return servers;
        }
    }

    private async Task<IReadOnlyList<McpServerConfig>> LoadDbServersAsync(IServiceProvider scopedProvider, CancellationToken ct)
    {
        List<McpServerRegistration> registrations;
        try
        {
            var repository = scopedProvider.GetRequiredService<IRepository<McpServerRegistration, Guid>>();
            registrations = await repository.AsQueryable()
                .Where(r => r.IsEnabled)
                .OrderByDescending(r => r.Priority)
                .ThenBy(r => r.Name)
                .ToListAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // fail-open 到部署配置：DB 不可用不应让基于 appsettings 的 MCP 服务器全部失效
            _logger.LogError(ex, "Failed to load MCP server registrations from database; falling back to deployment configuration only");
            return Array.Empty<McpServerConfig>();
        }

        var protector = _dataProtectionProvider.CreateProtector(McpServerRegistration.AuthTokenProtectorPurpose);
        var result = new List<McpServerConfig>(registrations.Count);
        foreach (var registration in registrations)
        {
            if (string.IsNullOrWhiteSpace(registration.Name))
            {
                continue;
            }

            if (!McpServerRegistrationMapper.IsAllowedTransport(registration.Transport))
            {
                // legacy stdio 行（schema 收紧前创建）或脏数据 - 跳过并告警
                _logger.LogWarning(
                    "MCP server registration '{ServerName}' has unsupported transport '{Transport}' and is skipped. " +
                    "Only sse / streamable-http / http registrations are materialized at runtime",
                    registration.Name, registration.Transport);
                continue;
            }

            try
            {
                result.Add(McpServerRegistrationMapper.ToServerConfig(registration, protector.Unprotect));
            }
            catch (Exception ex)
            {
                // 凭证解密失败（DataProtection key 轮换等）→ fail-closed 跳过该条目
                _logger.LogError(ex,
                    "Failed to materialize MCP server registration '{ServerName}' (credential decryption or mapping error); entry skipped",
                    registration.Name);
            }
        }

        return result;
    }
}
