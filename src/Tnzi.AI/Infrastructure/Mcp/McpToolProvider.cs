
namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// 从已配置的 MCP 服务器拉取工具，按 AllowedTools 过滤并按每服务器 ApprovalMode 做审批包装后返回。
/// 读取 AI:Mcp 配置（通过 AIOptions.Mcp），与 AgentFactory 一致；单个服务器不可用时记录警告并跳过，不阻塞整体。
/// </summary>
public class McpToolProvider : IMcpToolProvider
{
    private readonly IOptions<AIOptions> _options;
    private readonly IMcpClientFactory _clientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IToolApprovalHandler? _approvalHandler;
    private readonly ILogger<McpToolProvider> _logger;
    private readonly ConcurrentDictionary<string, ToolCacheEntry> _toolCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _toolLocks = new(StringComparer.OrdinalIgnoreCase);

    public McpToolProvider(
        IOptions<AIOptions> options,
        IMcpClientFactory clientFactory,
        ILoggerFactory loggerFactory,
        ILogger<McpToolProvider> logger,
        IToolApprovalHandler? approvalHandler = null)
    {
        _options = Check.NotNull(options);
        _clientFactory = Check.NotNull(clientFactory);
        _loggerFactory = Check.NotNull(loggerFactory);
        _logger = Check.NotNull(logger);
        _approvalHandler = approvalHandler;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken ct = default)
    {
        var mcp = _options.Value.Mcp;
        if (mcp == null || !mcp.Enabled || mcp.Servers == null || mcp.Servers.Count == 0)
        {
            return Array.Empty<AITool>();
        }

        var allTools = new List<AITool>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var server in mcp.Servers)
        {
            if (string.IsNullOrWhiteSpace(server.Name))
            {
                continue;
            }

            try
            {
                var tools = await GetToolsForServerAsync(server, mcp.ToolCacheSeconds, ct).ConfigureAwait(false);

                // Filter by AllowedTools (empty = allow all)
                var allowedSet = BuildNameSet(server.AllowedTools, server.Name, server.PrefixToolNameWithServer);
                var filtered = allowedSet.Count == 0
                    ? tools
                    : tools.Where(t => IsAllowedTool(t, allowedSet)).ToList();

                // Build per-server approval options from McpServerConfig. Approval only wraps tools that are AIFunction; non-AIFunction AITool are passed through without approval.
                var approvalOptions = BuildApprovalOptions(server);
                IList<AITool> toAdd = filtered is IList<AITool> list ? list : filtered.ToList();
                if (approvalOptions.Enabled)
                {
                    if (_approvalHandler != null)
                    {
                        var approvalLogger = _loggerFactory.CreateLogger<ApprovalToolWrapper>();
                        var toolNameToGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var t in toAdd)
                        {
                            if (t.Name != null)
                            {
                                toolNameToGroup[t.Name] = "mcp:" + server.Name;
                            }
                            var originalName = GetOriginalToolName(t);
                            if (!string.IsNullOrWhiteSpace(originalName))
                            {
                                toolNameToGroup[originalName] = "mcp:" + server.Name;
                            }
                        }
                        toAdd = ApprovalToolWrapper.Wrap(toAdd, _approvalHandler, approvalOptions, approvalLogger, toolNameToGroup);
                    }
                }

                // 审计：包装 AIFunction 以便在调用时记录 ServerName、ToolName、参数数量
                if (toAdd.Count > 0)
                {
                    var auditLogger = _loggerFactory.CreateLogger<McpAuditToolWrapper>();
                    toAdd = McpAuditToolWrapper.Wrap(toAdd, server.Name, auditLogger);
                }

                foreach (var tool in toAdd)
                {
                    var name = tool.Name ?? string.Empty;
                    if (seenNames.Add(name))
                    {
                        allTools.Add(tool);
                    }
                    else
                    {
                        _logger.LogDebug("MCP tool '{ToolName}' from server '{ServerName}' skipped (duplicate name)", name, server.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get tools from MCP server '{ServerName}'. Skipping.", server.Name);
            }
        }

        return allTools;
    }

    /// <summary>
    /// 根据 McpServerConfig 构造等效的 ToolApprovalOptions，用于 ApprovalToolWrapper。
    /// </summary>
    private static ToolApprovalOptions BuildApprovalOptions(McpServerConfig server)
    {
        var alwaysRequire = ExpandNameSet(server.AlwaysRequireApprovalTools, server.Name, server.PrefixToolNameWithServer);
        var neverRequire = ExpandNameSet(server.NeverRequireApprovalTools, server.Name, server.PrefixToolNameWithServer);
        var options = new ToolApprovalOptions
        {
            Enabled = server.ApprovalMode != McpApprovalMode.NeverRequire,
            Mode = server.ApprovalMode switch
            {
                McpApprovalMode.NeverRequire => ToolApprovalMode.NeverRequire,
                McpApprovalMode.AlwaysRequire => ToolApprovalMode.AlwaysRequire,
                McpApprovalMode.Specific => ToolApprovalMode.Specific,
                _ => ToolApprovalMode.NeverRequire
            },
            AlwaysRequireApproval = alwaysRequire,
            NeverRequireApproval = neverRequire,
            AlwaysRequireApprovalGroups = new List<string>(),
            TimeoutSeconds = 300
        };
        return options;
    }

    private async Task<IReadOnlyList<AITool>> GetToolsForServerAsync(
        McpServerConfig server,
        int cacheSeconds,
        CancellationToken ct)
    {
        if (cacheSeconds > 0 && _toolCache.TryGetValue(server.Name, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Tools;
        }

        var fetchLock = _toolLocks.GetOrAdd(server.Name, _ => new SemaphoreSlim(1, 1));
        await fetchLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (cacheSeconds > 0 && _toolCache.TryGetValue(server.Name, out cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return cached.Tools;
            }

            var adapter = await _clientFactory.GetOrCreateClientAsync(server, ct).ConfigureAwait(false);
            var tools = await adapter.ListToolsAsync(ct).ConfigureAwait(false);

            if (cacheSeconds > 0)
            {
                _toolCache[server.Name] = new ToolCacheEntry(
                    tools,
                    DateTimeOffset.UtcNow.AddSeconds(cacheSeconds));
            }

            return tools;
        }
        finally
        {
            fetchLock.Release();
        }
    }

    private static HashSet<string> BuildNameSet(
        List<string>? names,
        string serverName,
        bool prefixName)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (names == null || names.Count == 0)
        {
            return set;
        }

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            set.Add(name);
            if (prefixName && !name.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase))
            {
                set.Add($"mcp:{serverName}/{name}");
            }
        }
        return set;
    }

    private static List<string> ExpandNameSet(
        List<string>? names,
        string serverName,
        bool prefixName)
    {
        return BuildNameSet(names, serverName, prefixName).ToList();
    }

    private static bool IsAllowedTool(AITool tool, HashSet<string> allowedSet)
    {
        if (allowedSet.Count == 0)
        {
            return true;
        }

        var name = tool.Name ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(name) && allowedSet.Contains(name))
        {
            return true;
        }

        var original = GetOriginalToolName(tool);
        return !string.IsNullOrWhiteSpace(original) && allowedSet.Contains(original);
    }

    private static string? GetOriginalToolName(AITool tool)
    {
        if (tool?.AdditionalProperties == null)
        {
            return null;
        }

        if (tool.AdditionalProperties.TryGetValue("mcp.originalName", out var value))
        {
            return value as string;
        }

        return null;
    }

    private sealed class ToolCacheEntry
    {
        public ToolCacheEntry(IReadOnlyList<AITool> tools, DateTimeOffset expiresAt)
        {
            Tools = tools;
            ExpiresAt = expiresAt;
        }

        public IReadOnlyList<AITool> Tools { get; }
        public DateTimeOffset ExpiresAt { get; }
    }
}
