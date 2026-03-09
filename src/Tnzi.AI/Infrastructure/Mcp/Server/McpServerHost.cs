using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Tnzi.AI.Infrastructure.Mcp.Server;

/// <summary>
/// MCP Server Host 实现 — 将 Agent 和自定义工具暴露为 MCP Server
/// </summary>
public class McpServerHost : IMcpServerHost
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<Options.McpServerOptions> _options;
    private readonly ILogger<McpServerHost> _logger;
    private readonly McpServerSecurityMiddleware _security;

    // 已注册的 Agent 工具
    private readonly ConcurrentDictionary<Guid, AgentToolRegistration> _agentTools = new();

    // 已注册的自定义工具
    private readonly ConcurrentDictionary<string, CustomToolRegistration> _customTools = new(StringComparer.OrdinalIgnoreCase);

    public McpServerHost(
        IServiceProvider serviceProvider,
        IOptions<Options.McpServerOptions> options,
        ILogger<McpServerHost> logger,
        McpServerSecurityMiddleware security)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _security = Check.NotNull(security);
    }

    /// <inheritdoc />
    public void ExposeAgent(Guid agentId, McpToolExposureOptions? options = null)
    {
        _agentTools[agentId] = new AgentToolRegistration(agentId, options);
        _logger.LogInformation("Registered Agent '{AgentId}' for MCP exposure", agentId);
    }

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

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var config = _options.Value;
        if (!config.Enabled)
        {
            _logger.LogDebug("MCP Server is disabled, skipping start");
            return;
        }

        if (config.RequireAuthentication && config.Transport == "stdio")
        {
            _logger.LogWarning("MCP Server RequireAuthentication is enabled with stdio transport. " +
                "Authentication is enforced at the process boundary for stdio transport. " +
                "Use SSE transport for HTTP-level API key authentication.");
        }

        EnsureConfiguredAgentsExposed();

        // 构建 MCP Server 工具列表
        var tools = await BuildToolsAsync(cancellationToken);
        if (tools.Count == 0)
        {
            _logger.LogWarning("MCP Server has no tools to expose, skipping start");
            return;
        }

        if (!string.Equals(config.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "MCP Server transport '{Transport}' requires ASP.NET Core endpoint integration and is not started by the hosted service. Configure endpoint mapping separately.",
                config.Transport);
            return;
        }

        // 使用 ModelContextProtocol SDK 构建并启动 Server
        var serverServices = new ServiceCollection();
        serverServices.AddLogging();
        var builder = serverServices.AddMcpServer(serverOptions =>
        {
            serverOptions.ServerInfo = new()
            {
                Name = "Tnzi.AI",
                Version = typeof(McpServerHost).Assembly.GetName().Version?.ToString() ?? "1.0.0"
            };
        });

        // stdio 传输使用 WithStdioServerTransport 注册 SingleSessionMcpServerHostedService
        builder.WithStdioServerTransport();
        builder.WithTools(tools);

        await using var serverProvider = serverServices.BuildServiceProvider();
        var hostedServices = serverProvider.GetServices<IHostedService>().ToList();
        if (hostedServices.Count == 0)
        {
            throw new InvalidOperationException("MCP Server host was built without any registered hosted services.");
        }

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(cancellationToken);
        }

        _logger.LogInformation(
            "MCP Server started with {ToolCount} tools, transport: {Transport}",
            tools.Count, config.Transport);

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("MCP Server shutting down");
        }
        finally
        {
            foreach (var hostedService in hostedServices.AsEnumerable().Reverse())
            {
                try
                {
                    await hostedService.StopAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to stop MCP hosted service cleanly.");
                }
            }
        }
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
    /// 将 Agent 构建为 MCP 工具（Agent 工具调用路由到 IAgentRuntime.RunAsync）
    /// </summary>
    private async Task<McpServerTool?> BuildAgentToolAsync(
        Guid agentId,
        AgentToolRegistration registration,
        CancellationToken ct)
    {
        // 从数据库加载 Agent 信息（用 scope 以获取 scoped 服务）
        using var scope = _serviceProvider.CreateScope();
        var agentService = scope.ServiceProvider.GetRequiredService<IAgentService>();
        var agentResult = await agentService.GetByIdAsync(agentId);
        if (!agentResult.Succeeded || agentResult.Data == null)
        {
            _logger.LogWarning("Agent '{AgentId}' not found, skipping MCP exposure", agentId);
            return null;
        }

        var agent = agentResult.Data;
        var toolName = registration.Options?.ToolName ?? SanitizeToolName(agent.Name);
        var description = registration.Options?.Description ?? agent.Description ?? $"Run AI Agent: {agent.Name}";

        // 捕获 agentId 和 toolName 到闭包
        var capturedAgentId = agentId;
        var capturedToolName = toolName;

        // 使用 McpServerTool.Create(Delegate) 创建工具
        Func<string, CancellationToken, Task<string>> handler = (message, cancellation) =>
            InvokeAgentAsync(capturedAgentId, capturedToolName, message, cancellation);

        return McpServerTool.Create(handler, new McpServerToolCreateOptions
        {
            Name = toolName,
            Description = description
        });
    }

    /// <summary>
    /// 调用 Agent（通过 IAgentRuntime），包含安全检查
    /// </summary>
    private async Task<string> InvokeAgentAsync(
        Guid agentId,
        string toolName,
        string message,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        var isSuccess = false;

        try
        {
            // 速率限制检查
            if (!_security.CheckRateLimit($"agent:{agentId}"))
            {
                throw new RateLimitException("Rate limit exceeded");
            }

            // 通过 scoped IAgentRuntime 执行
            using var scope = _serviceProvider.CreateScope();
            var runtime = scope.ServiceProvider.GetRequiredService<IAgentRuntime>();

            var request = new AgentRunRequest
            {
                AgentId = agentId,
                UserMessage = message
            };

            var result = await runtime.RunAsync(request, ct);
            isSuccess = true;
            return result.Response;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            _logger.LogError(ex, "MCP tool call failed for Agent '{AgentId}'", agentId);
            // 仅暴露业务异常消息，内部错误使用通用提示
            return ex is BusinessException
                ? $"Error: {ex.Message}"
                : "Error: An internal error occurred while processing the request.";
        }
        finally
        {
            sw.Stop();
            await _security.AuditLogAsync(toolName, agentId, sw.ElapsedMilliseconds, isSuccess, errorMessage, CancellationToken.None);
        }
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
                    capturedName, agentId: null, sw.ElapsedMilliseconds, isSuccess, errorMessage, CancellationToken.None);
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
                cancellationToken);
        }
    }

    private async Task<AgentToolMatch?> ResolveAgentToolAsync(string toolName, CancellationToken cancellationToken)
    {
        foreach (var (agentId, registration) in _agentTools)
        {
            var metadata = await GetAgentToolMetadataAsync(agentId, registration, cancellationToken);
            if (metadata != null
                && string.Equals(metadata.Value.ToolName, toolName, StringComparison.OrdinalIgnoreCase))
            {
                return new AgentToolMatch(agentId, metadata.Value.ToolName);
            }
        }

        return null;
    }

    private async Task<(string ToolName, string Description)?> GetAgentToolMetadataAsync(
        Guid agentId,
        AgentToolRegistration registration,
        CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var agentService = scope.ServiceProvider.GetRequiredService<IAgentService>();
        var agentResult = await agentService.GetByIdAsync(agentId);
        if (!agentResult.Succeeded || agentResult.Data == null)
        {
            return null;
        }

        var agent = agentResult.Data;
        var toolName = registration.Options?.ToolName ?? SanitizeToolName(agent.Name);
        var description = registration.Options?.Description ?? agent.Description ?? $"Run AI Agent: {agent.Name}";
        return (toolName, description);
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
