



namespace Tnzi.AI.Mcp.Server;

/// <summary>
/// MCP Server Host 实现 — 将 Agent 和自定义工具暴露为 MCP Server
/// </summary>
public partial class McpServerHost : IMcpServerHost
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<McpServerOptions> _options;
    private readonly ILogger<McpServerHost> _logger;
    private readonly McpServerSecurityMiddleware _security;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    // 已注册的 Agent 工具
    private readonly ConcurrentDictionary<Guid, AgentToolRegistration> _agentTools = new();

    // 已注册的自定义工具
    private readonly ConcurrentDictionary<string, CustomToolRegistration> _customTools = new(StringComparer.OrdinalIgnoreCase);

    // 工具名 → AgentId 缓存（BuildToolsAsync 时填充，避免 CallToolAsync 逐个查库）
    private readonly ConcurrentDictionary<string, Guid> _agentToolNameMap = new(StringComparer.OrdinalIgnoreCase);

    public McpServerHost(
        IServiceProvider serviceProvider,
        IOptions<McpServerOptions> options,
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
        var response = await InvokeAgentAsync(agentRegistration.AgentId, name, message, cancellationToken);
        return CreateTextResult(response, isError: response.StartsWith("Error:", StringComparison.Ordinal));
    }

    private void EnsureConfiguredAgentsExposed()
    {
        foreach (var agentId in _options.Value.ExposedAgentIds)
        {
            if (!_agentTools.ContainsKey(agentId))
            {
                ExposeAgent(agentId);
            }
        }
    }

    /// <summary>
    /// 构建所有 MCP 工具
    /// </summary>
    private async Task<List<McpServerTool>> BuildToolsAsync(CancellationToken ct)
    {
        var tools = new List<McpServerTool>();

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
    /// 构建自定义 MCP 工具
    /// </summary>
    private McpServerTool BuildCustomTool(CustomToolRegistration registration)
    {
        var capturedName = registration.Name;
        var capturedHandler = registration.Handler;

        Func<string, CancellationToken, Task<string>> handler = async (input, ct) =>
        {
            var sw = Stopwatch.StartNew();
            string? errorMessage = null;
            var isSuccess = false;

            try
            {
                // 速率限制
                if (!_security.CheckRateLimit($"tool:{capturedName}"))
                {
                    throw new RateLimitException("Rate limit exceeded");
                }

                JsonElement inputElement;
                try
                {
                    inputElement = JsonDocument.Parse(input).RootElement;
                }
                catch
                {
                    inputElement = JsonSerializer.SerializeToElement(input);
                }

                var result = await capturedHandler(inputElement);
                isSuccess = true;
                return result;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                _logger.LogError(ex, "MCP custom tool call failed for '{ToolName}'", capturedName);
                // 仅暴露业务异常消息，内部错误使用通用提示
                return ex is BusinessException
                    ? $"Error: {ex.Message}"
                    : "Error: An internal error occurred while processing the request.";
            }
            finally
            {
                sw.Stop();
                await _security.AuditLogAsync(
                    capturedName, agentId: null, sw.ElapsedMilliseconds, isSuccess, errorMessage,
                    callerApiKeyId: GetCallerHash(), ct: CancellationToken.None);
            }
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
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        var isSuccess = false;

        try
        {
            if (!_security.CheckRateLimit($"tool:{registration.Name}"))
            {
                throw new RateLimitException("Rate limit exceeded");
            }

            var payload = arguments == null
                ? JsonDocument.Parse("{}").RootElement.Clone()
                : JsonSerializer.SerializeToElement(arguments);

            var result = await registration.Handler(payload);
            isSuccess = true;
            return CreateTextResult(result, isError: false);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            _logger.LogError(ex, "MCP custom tool call failed for '{ToolName}'", registration.Name);
            return CreateErrorResult(ex is BusinessException
                ? $"Error: {ex.Message}"
                : "Error: An internal error occurred while processing the request.");
        }
        finally
        {
            sw.Stop();
            await _security.AuditLogAsync(
                registration.Name,
                agentId: null,
                sw.ElapsedMilliseconds,
                isSuccess,
                errorMessage,
                callerApiKeyId: GetCallerHash(),
                ct: cancellationToken);
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
