// 与 ModelContextProtocol.Protocol.Result 同名
using Result = Tnzi.Results.Result;

namespace Tnzi.AI.Mcp.Server;

/// <summary>
/// MCP Server Host - Agent 暴露与调用相关方法
/// </summary>
public partial class McpServerHost
{
    /// <inheritdoc />
    /// <remarks>
    /// 进程内注册，不做存在性检查（配置预暴露与消费方代码走这里）；指定的工具名不合规当场抛出。
    /// 管理端走 <see cref="ExposeAgentAsync"/>，它先确认调用方看得见这个 Agent 且已启用。
    /// </remarks>
    public void ExposeAgent(Guid agentId, McpToolExposureOptions? options = null)
    {
        if (InvalidToolNameMessage(options) is { } message)
        {
            throw new ArgumentException(message, nameof(options));
        }

        _agentTools[agentId] = new AgentToolRegistration(agentId, options);
        InvalidateToolCache();
        _logger.LogInformation("Registered Agent '{AgentId}' for MCP exposure", agentId);
    }

    /// <inheritdoc />
    /// <remarks>
    /// ★ 此前管理端暴露一个不存在（或调用方看不见）的 Agent 也答 200：已暴露列表里有它，<c>tools/list</c> 却永远没有它，
    /// 管理页看不出原因。现在在调用方的租户作用域里先解析：找不到答 404，已停用答 400，都不写入注册表。
    /// </remarks>
    public async Task<Result> ExposeAgentAsync(
        Guid agentId, McpToolExposureOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (InvalidToolNameMessage(options) is { } message)
        {
            return Result.Failure(message, 400);
        }

        using (var scope = _serviceProvider.CreateScope())
        {
            var agentService = scope.ServiceProvider.GetRequiredService<IAgentService>();
            var agent = await agentService.GetByIdAsync(agentId);
            if (!agent.Succeeded || agent.Data is null)
            {
                return Result.Failure("Agent not found.", 404, ErrorCodes.AgentNotFound);
            }

            if (!agent.Data.IsEnabled)
            {
                return Result.Failure("The agent is disabled.", 400, ErrorCodes.AgentDisabled);
            }
        }

        ExposeAgent(agentId, options);
        return Result.Success();
    }

    /// <inheritdoc />
    public bool RemoveAgent(Guid agentId)
    {
        var removed = _agentTools.TryRemove(agentId, out _);
        if (removed)
        {
            // 各租户的工具集连同工具名映射一起作废：按名解析走的是同一份工具集，不会再命中被移除的 Agent
            InvalidateToolCache();
            _logger.LogInformation("Removed Agent '{AgentId}' from MCP exposure", agentId);
        }
        return removed;
    }

    /// <inheritdoc />
    public IReadOnlyList<Guid> GetExposedAgentIds() => [.. _agentTools.Keys];

    private static string? InvalidToolNameMessage(McpToolExposureOptions? options)
        => options?.ToolName is { } toolName
           && !string.IsNullOrWhiteSpace(toolName)
           && !McpToolNames.IsValidAgentToolName(toolName)
            ? $"MCP tool name '{toolName}' is invalid: use 1-{McpToolNames.MaxAgentToolNameLength} characters from A-Z, a-z, 0-9, '_' and '-'."
            : null;

    /// <summary>
    /// 在指定有效租户下解析一个暴露的 Agent：工具名（还没去重）与描述。Agent 在这个租户下看不见时返回 null。
    /// </summary>
    /// <remarks>
    /// ★ 与执行同一个租户口径：Agent 是多租户实体，此前在无租户的根作用域里查，运行范围凭据所属租户的 agent
    /// 一律「not found」被跳过，tools/list 为空、tools/call 答 not exposed，凭据对租户 agent 从来不可用。
    /// </remarks>
    private async Task<AgentToolSpec?> LoadAgentToolSpecAsync(Guid agentId, AgentToolRegistration registration, Guid? tenantId)
    {
        // 从数据库加载 Agent 信息（用 scope 以获取 scoped 服务），在有效租户下解析
        using var scope = _serviceProvider.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetService<ICurrentTenant>()?.Change(tenantId);
        var agentService = scope.ServiceProvider.GetRequiredService<IAgentService>();
        var agentResult = await agentService.GetByIdAsync(agentId);
        if (!agentResult.Succeeded || agentResult.Data == null)
        {
            _logger.LogWarning("Agent '{AgentId}' not found for tenant {TenantId}, skipping MCP exposure", agentId, tenantId);
            return null;
        }

        var agent = agentResult.Data;
        var toolName = string.IsNullOrWhiteSpace(registration.Options?.ToolName)
            ? McpToolNames.FromAgentName(agent.Name, agentId)
            : registration.Options!.ToolName!;
        var description = registration.Options?.Description ?? agent.Description ?? $"Run AI Agent: {agent.Name}";

        return new AgentToolSpec(agentId, toolName, description);
    }

    /// <summary>
    /// 调用 Agent（通过 IAgentRuntime），经统一守卫处理限流/审计/异常映射。
    /// </summary>
    /// <param name="agentId">目标 Agent。</param>
    /// <param name="toolName">MCP 工具名（审计 / 限流键）。</param>
    /// <param name="message">用户消息。</param>
    /// <param name="tenantId">执行租户：运行范围凭据自带的可信租户；静态 key 为 null（宿主作用域）。</param>
    /// <param name="cancellationToken">取消令牌；守卫再把它与承载这次调用的 HTTP 请求连起来。</param>
    private Task<CallToolResult> InvokeAgentAsync(
        Guid agentId,
        string toolName,
        string message,
        Guid? tenantId,
        CancellationToken cancellationToken) =>
        ExecuteWithGuardsAsync(toolName, agentId, async ct =>
        {
            // 通过 scoped IAgentRuntime 执行
            using var scope = _serviceProvider.CreateScope();
            // 运行范围凭据带来的是唯一可信的租户来源（签发时的运行记录）；静态 key 显式切到宿主，
            // 不继承请求头带来的环境租户（客户端可控，不是调用方身份）
            using var tenantScope = scope.ServiceProvider.GetService<ICurrentTenant>()?.Change(tenantId);
            var runtime = scope.ServiceProvider.GetRequiredService<IAgentRuntime>();

            var request = new AgentRunRequest
            {
                AgentId = agentId,
                UserMessage = message
            };

            var result = await runtime.RunAsync(request, ct);

            // 护栏拒绝、配额超限、交接超限、运行失败都以结果（FinishReason）返回而不是抛出：
            // 不在这里判，守卫只看有没有异常，客户端收到 IsError=false、把拒绝文案当成工具的真实输出继续推理，
            // 调用记录也记成功。判据与 HTTP 端点同一个（AgentStreamMapper.TryMapFailure），文案同样是运行给出的那句。
            if (AgentStreamMapper.TryMapFailure(result, ErrorCodes.AgentRunFailed, out var statusCode, out var errorCode))
            {
                throw new BusinessException(
                    string.IsNullOrWhiteSpace(result.Response) ? "The agent run did not complete." : result.Response,
                    errorCode,
                    statusCode);
            }

            return CreateTextResult(result.Response, isError: false);
        }, cancellationToken);
}
