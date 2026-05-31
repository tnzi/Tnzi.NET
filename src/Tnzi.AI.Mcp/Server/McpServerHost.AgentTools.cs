namespace Tnzi.AI.Mcp.Server;

/// <summary>
/// MCP Server Host — Agent 暴露与调用相关方法
/// </summary>
public partial class McpServerHost
{
    /// <inheritdoc />
    public void ExposeAgent(Guid agentId, McpToolExposureOptions? options = null)
    {
        _agentTools[agentId] = new AgentToolRegistration(agentId, options);
        _logger.LogInformation("Registered Agent '{AgentId}' for MCP exposure", agentId);
    }

    /// <inheritdoc />
    public bool RemoveAgent(Guid agentId)
    {
        var removed = _agentTools.TryRemove(agentId, out _);
        if (removed)
        {
            // 清理工具名缓存中该 Agent 对应的条目
            var keysToRemove = _agentToolNameMap.Where(kv => kv.Value == agentId).Select(kv => kv.Key).ToList();
            foreach (var key in keysToRemove)
                _agentToolNameMap.TryRemove(key, out _);

            _logger.LogInformation("Removed Agent '{AgentId}' from MCP exposure", agentId);
        }
        return removed;
    }

    /// <inheritdoc />
    public IReadOnlyList<Guid> GetExposedAgentIds() => [.. _agentTools.Keys];

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

        // 缓存 toolName → agentId 映射
        _agentToolNameMap[toolName] = agentId;

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
            await _security.AuditLogAsync(
                toolName, agentId, sw.ElapsedMilliseconds, isSuccess, errorMessage,
                callerApiKeyId: GetCallerHash(), ct: CancellationToken.None);
        }
    }
}
