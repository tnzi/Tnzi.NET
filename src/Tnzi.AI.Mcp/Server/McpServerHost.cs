



namespace Tnzi.AI.Mcp.Server;

/// <summary>
/// MCP Server Host 实现 - 将 Agent 和自定义工具暴露为 MCP Server
/// </summary>
public partial class McpServerHost : IMcpServerHost
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<McpServerOptions> _options;
    private readonly ILogger<McpServerHost> _logger;
    private readonly McpServerSecurityMiddleware _security;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    // 已注册的 Agent 工具
    private readonly ConcurrentDictionary<Guid, AgentToolRegistration> _agentTools = new();

    // 已注册的自定义工具
    private readonly ConcurrentDictionary<string, CustomToolRegistration> _customTools = new(StringComparer.OrdinalIgnoreCase);

    // 工具名 → AgentId 缓存（BuildToolsAsync 时填充，避免 CallToolAsync 逐个查库）
    private readonly ConcurrentDictionary<string, Guid> _agentToolNameMap = new(StringComparer.OrdinalIgnoreCase);

    // 构建结果缓存：避免每次 tools/list 都对每个 Agent 重新建 scope + GetByIdAsync（N+1）。
    // 失效驱动（ExposeAgent/RemoveAgent/ExposeTool bump 版本号）为主，TTL 兜底防止配置外漂移。
    private static readonly TimeSpan ToolCacheTtl = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private long _registrationVersion;          // 每次 expose/remove 自增
    private List<McpServerTool>? _cachedTools;   // 受 _buildLock 保护
    private long _cachedVersion = -1;            // 缓存对应的注册版本
    private long _cachedAtTicks;                 // 缓存构建时刻 UTC ticks（原子读写，避免 DateTimeOffset 撕裂）

    /// <summary>
    /// Bumps the registration version, invalidating the built-tool cache on the next
    /// <see cref="ListToolsAsync"/>/<see cref="CallToolAsync"/>. Called whenever the set of
    /// exposed agents or custom tools changes.
    /// </summary>
    private void InvalidateToolCache() => Interlocked.Increment(ref _registrationVersion);

    public McpServerHost(
        IServiceProvider serviceProvider,
        IOptionsMonitor<McpServerOptions> options,
        ILogger<McpServerHost> logger,
        McpServerSecurityMiddleware security,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _security = Check.NotNull(security);
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// 从当前 HTTP 请求上下文中读取调用方哈希摘要（由 <see cref="McpServerHttpSecurityMiddleware"/> 存入）。
    /// </summary>
    private string? GetCallerHash() =>
        _httpContextAccessor?.HttpContext?.Items[McpServerSecurityMiddleware.CallerHashItemKey] as string;

    /// <inheritdoc />
    public IReadOnlyList<string> GetCustomToolNames() => [.. _customTools.Keys];

    /// <inheritdoc />
    public void ExposeTool(string name, string description, Func<JsonElement, Task<string>> handler)
    {
        Check.NotNullOrWhiteSpace(name);
        Check.NotNull(handler);

        _customTools[name] = new CustomToolRegistration(name, description, handler);
        InvalidateToolCache();
        _logger.LogInformation("Registered custom MCP tool '{ToolName}'", name);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Tool>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfiguredAgentsExposed();
        var tools = await BuildToolsAsync(cancellationToken);
        return tools.Select(x => x.ProtocolTool).ToList();
    }

    /// <inheritdoc />
    public async Task<CallToolResult> CallToolAsync(
        string name,
        IDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(name);
        EnsureConfiguredAgentsExposed();

        if (_customTools.TryGetValue(name, out var customTool))
        {
            return await InvokeCustomToolAsync(customTool, arguments, cancellationToken);
        }

        var agentRegistration = await ResolveAgentToolAsync(name, cancellationToken);
        if (agentRegistration == null)
        {
            return CreateErrorResult($"Tool '{name}' is not exposed by MCP Server.");
        }

        var message = ExtractMessage(arguments);
        var (response, isError) = await InvokeAgentAsync(agentRegistration.AgentId, name, message, cancellationToken);
        return CreateTextResult(response, isError);
    }

    private void EnsureConfiguredAgentsExposed()
    {
        foreach (var agentId in _options.CurrentValue.ExposedAgentIds)
        {
            if (!_agentTools.ContainsKey(agentId))
            {
                ExposeAgent(agentId);
            }
        }
    }

    /// <summary>
    /// 构建所有 MCP 工具（带缓存）。失效驱动（注册版本号）为主，30s TTL 兜底。
    /// 避免每次 tools/list / 冷缓存 tools/call 都对每个 Agent 重新建 scope + 查库（N+1）。
    /// </summary>
    private async Task<List<McpServerTool>> BuildToolsAsync(CancellationToken ct)
    {
        var version = Interlocked.Read(ref _registrationVersion);

        // 快路径：版本未变且 TTL 未过期，直接返回缓存（无锁读 - 引用赋值原子）
        var cached = _cachedTools;
        if (cached != null
            && Interlocked.Read(ref _cachedVersion) == version
            && DateTimeOffset.UtcNow.UtcTicks - Interlocked.Read(ref _cachedAtTicks) < ToolCacheTtl.Ticks)
        {
            return cached;
        }

        await _buildLock.WaitAsync(ct);
        try
        {
            // 双检：可能已有其他请求在等锁期间重建
            version = Interlocked.Read(ref _registrationVersion);
            if (_cachedTools != null
                && _cachedVersion == version
                && DateTimeOffset.UtcNow.UtcTicks - _cachedAtTicks < ToolCacheTtl.Ticks)
            {
                return _cachedTools;
            }

            var tools = await BuildToolsUncachedAsync(ct);

            _cachedTools = tools;
            Interlocked.Exchange(ref _cachedAtTicks, DateTimeOffset.UtcNow.UtcTicks);
            Interlocked.Exchange(ref _cachedVersion, version);   // publish version last (gate)

            return tools;
        }
        finally
        {
            _buildLock.Release();
        }
    }

    /// <summary>
    /// 实际构建所有 MCP 工具（无缓存）。同时填充 <see cref="_agentToolNameMap"/>。
    /// </summary>
    private async Task<List<McpServerTool>> BuildToolsUncachedAsync(CancellationToken ct)
    {
        var tools = new List<McpServerTool>();

        // 全量重建 name-map（在 _buildLock 内），避免 re-expose 改名后残留旧映射
        _agentToolNameMap.Clear();

        // 构建 Agent 工具
        foreach (var (agentId, registration) in _agentTools)
        {
            try
            {
                var tool = await BuildAgentToolAsync(agentId, registration, ct);
                if (tool != null)
                {
                    tools.Add(tool);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build MCP tool for Agent '{AgentId}'. Skipping.", agentId);
            }
        }

        // 构建自定义工具
        foreach (var (_, registration) in _customTools)
        {
            try
            {
                var tool = BuildCustomTool(registration);
                tools.Add(tool);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build custom MCP tool '{ToolName}'. Skipping.", registration.Name);
            }
        }

        return tools;
    }

    /// <summary>
    /// 构建自定义 MCP 工具（仅用于 <c>tools/list</c> 元数据）。
    /// <para>
    /// <b>执行路径说明：</b> 真实 HTTP <c>tools/call</c> 始终经
    /// <see cref="CallToolAsync"/> → <see cref="InvokeCustomToolAsync"/> 调度，
    /// 该 <see cref="McpServerTool"/> 实例的 handler 闭包在 HTTP 传输下不会被触发。
    /// 为保持单一执行路径并防御 SDK 直调，handler 与 <see cref="InvokeCustomToolAsync"/>
    /// 复用同一 <see cref="ExecuteWithGuardsAsync"/> 守卫实现（不再重复限流/审计逻辑）。
    /// </para>
    /// </summary>
    private McpServerTool BuildCustomTool(CustomToolRegistration registration)
    {
        var capturedRegistration = registration;

        Func<string, CancellationToken, Task<string>> handler = async (input, ct) =>
        {
            JsonElement inputElement;
            try
            {
                inputElement = JsonDocument.Parse(input).RootElement;
            }
            catch
            {
                inputElement = JsonSerializer.SerializeToElement(input);
            }

            var (text, _) = await ExecuteWithGuardsAsync(
                capturedRegistration.Name,
                agentId: null,
                () => capturedRegistration.Handler(inputElement),
                ct);
            return text;
        };

        return McpServerTool.Create(handler, new McpServerToolCreateOptions
        {
            Name = registration.Name,
            Description = registration.Description
        });
    }

    private async Task<CallToolResult> InvokeCustomToolAsync(
        CustomToolRegistration registration,
        IDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken)
    {
        var payload = arguments == null
            ? JsonDocument.Parse("{}").RootElement.Clone()
            : JsonSerializer.SerializeToElement(arguments);

        var (text, isError) = await ExecuteWithGuardsAsync(
            registration.Name,
            agentId: null,
            () => registration.Handler(payload),
            cancellationToken);

        return CreateTextResult(text, isError);
    }

    /// <summary>
    /// 单一执行守卫：速率限制 → 执行 → 异常映射 → 审计/分析记录。
    /// Agent 与自定义工具的所有真实调用均经此处，消除多路径下重复的限流/审计逻辑。
    /// <para>
    /// 成功/失败由是否抛异常权威判定（<paramref name="IsError"/>），而非对结果文本嗅探
    /// <c>"Error:"</c> 前缀——避免合法工具结果恰好以 <c>"Error:"</c> 开头时被误判。
    /// </para>
    /// </summary>
    /// <returns>(结果文本, 是否为错误)；错误时文本为 <c>"Error: ..."</c>（业务异常透出消息，内部错误用通用提示）。</returns>
    private async Task<(string Text, bool IsError)> ExecuteWithGuardsAsync(
        string toolName,
        Guid? agentId,
        Func<Task<string>> execute,
        CancellationToken ct)
    {
        var rateLimitKey = agentId.HasValue ? $"agent:{agentId.Value}" : $"tool:{toolName}";
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        var isSuccess = false;

        try
        {
            if (!_security.CheckRateLimit(rateLimitKey))
            {
                throw new RateLimitException("Rate limit exceeded");
            }

            var result = await execute();
            isSuccess = true;
            return (result, false);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            _logger.LogError(ex, "MCP tool call failed for '{ToolName}'", toolName);
            // 仅暴露业务异常消息，内部错误使用通用提示
            var text = ex is BusinessException
                ? $"Error: {ex.Message}"
                : "Error: An internal error occurred while processing the request.";
            return (text, true);
        }
        finally
        {
            sw.Stop();
            await _security.AuditLogAsync(
                toolName, agentId, sw.ElapsedMilliseconds, isSuccess, errorMessage,
                callerApiKeyId: GetCallerHash(), ct: ct);
        }
    }

    private async Task<AgentToolMatch?> ResolveAgentToolAsync(string toolName, CancellationToken cancellationToken)
    {
        // 优先从缓存查找（BuildToolsAsync 时已填充）
        if (_agentToolNameMap.TryGetValue(toolName, out var cachedAgentId))
        {
            return new AgentToolMatch(cachedAgentId, toolName);
        }

        // Cold-cache path: name-map not yet populated (no prior ListToolsAsync call).
        // Lazily build the tool list to populate _agentToolNameMap, then retry.
        await BuildToolsAsync(cancellationToken);

        if (_agentToolNameMap.TryGetValue(toolName, out var resolvedAgentId))
        {
            return new AgentToolMatch(resolvedAgentId, toolName);
        }

        return null;
    }

    private static string ExtractMessage(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || arguments.Count == 0)
        {
            return string.Empty;
        }

        if (arguments.TryGetValue("message", out var messageElement)
            && messageElement.ValueKind == JsonValueKind.String)
        {
            return messageElement.GetString() ?? string.Empty;
        }

        if (arguments.TryGetValue("input", out var inputElement)
            && inputElement.ValueKind == JsonValueKind.String)
        {
            return inputElement.GetString() ?? string.Empty;
        }

        if (arguments.Count == 1)
        {
            var onlyValue = arguments.Values.First();
            return onlyValue.ValueKind == JsonValueKind.String
                ? onlyValue.GetString() ?? string.Empty
                : onlyValue.GetRawText();
        }

        return JsonSerializer.Serialize(arguments);
    }

    private static CallToolResult CreateTextResult(string text, bool isError)
    {
        var result = new CallToolResult
        {
            IsError = isError,
            Content =
            [
                new TextContentBlock { Text = text }
            ]
        };

        if (TryParseJson(text, out var structuredContent))
        {
            result.StructuredContent = structuredContent;
        }

        return result;
    }

    private static CallToolResult CreateErrorResult(string message) => CreateTextResult(message, isError: true);

    private static bool TryParseJson(string value, out JsonElement structuredContent)
    {
        try
        {
            structuredContent = JsonDocument.Parse(value).RootElement.Clone();
            return true;
        }
        catch
        {
            structuredContent = default;
            return false;
        }
    }

    /// <summary>
    /// 清理工具名称（MCP 工具名只允许字母、数字、下划线、连字符）
    /// </summary>
    private static string SanitizeToolName(string name)
    {
        var sanitized = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
            {
                sanitized.Append(c);
            }
            else if (c == ' ')
            {
                sanitized.Append('_');
            }
        }

        var result = sanitized.ToString();
        return string.IsNullOrWhiteSpace(result) ? "agent_tool" : result;
    }

    private sealed record AgentToolRegistration(Guid AgentId, McpToolExposureOptions? Options);

    private sealed record CustomToolRegistration(string Name, string Description, Func<JsonElement, Task<string>> Handler);

    private sealed record AgentToolMatch(Guid AgentId, string ToolName);
}
