using Microsoft.Extensions.AI;

namespace Tnzi.AI.Mcp.Server;

/// <summary>
/// 转发：外部 CLI 持运行范围凭据（带 <see cref="RunScopedCredential.UserMcpTools"/>）来调用时，
/// 平台把派发者自己连接的远程 MCP 服务器的工具并进 <c>tools/list</c>，并把 <c>tools/call</c> 转发过去。
/// </summary>
/// <remarks>
/// <para>
/// 令牌始终留在平台：CLI 只拿到工具名与结果。工具来自 <see cref="IMcpToolProvider.GetUserToolsAsync"/>，
/// 与内建路径是同一批包装（审批 / 审计 / 401 恢复 / egress 守卫）与同一套按用户分区的连接缓存。
/// </para>
/// <para>
/// 用户由凭据里运行记录的 <see cref="McpCallerScope.CreatorId"/> 决定，不取任何请求头。工具名按
/// <c>{server}__{tool}</c> 前缀（<c>McpServerConfig.PrefixToolNameWithServer</c>）区分，
/// 与平台自己的工具撞名时以平台的为准。
/// </para>
/// <para>
/// 外部 CLI 是无人值守的：服务器配了需要审批的模式（默认 <c>McpApprovalMode.AlwaysRequire</c>）而没有可答复的
/// 审批人时，调用被审批包装拒绝（失败关闭）。消费方对用户已明确授权的服务器要显式放宽。
/// </para>
/// </remarks>
public partial class McpServerHost
{
    private readonly record struct UserRelayTool(string Name, Tool Tool, AIFunction Function);

    private async Task<IReadOnlyList<UserRelayTool>> ListUserToolsAsync(McpCallerScope caller, CancellationToken ct)
    {
        var provider = _serviceProvider.GetService<IMcpToolProvider>();
        if (provider is null || caller.CreatorId is not { } userId)
        {
            return [];
        }

        IReadOnlyList<AITool> tools;
        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            // 与自定义工具相同：显式切到凭据的租户（含 null），不继承请求头带来的环境租户
            using var tenantScope = scope.ServiceProvider.GetService<ICurrentTenant>()?.Change(caller.TenantId);
            tools = await provider.GetUserToolsAsync(userId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Failed to list the user-scoped MCP tools for a run-scoped caller; none are offered");
            return [];
        }

        var result = new List<UserRelayTool>();
        foreach (var function in tools.OfType<AIFunction>())
        {
            try
            {
                // Tool.InputSchema 的 setter 按协议校验顶层 type；不合规的条目跳过，不拖垮整份清单
                var tool = new Tool { Name = function.Name, Description = function.Description, InputSchema = function.JsonSchema };
                result.Add(new UserRelayTool(function.Name, tool, function));
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Skipping user-scoped MCP tool '{ToolName}' with an invalid input schema", function.Name);
            }
        }

        return result;
    }

    /// <summary>
    /// 把调用转发给派发者的用户服务器。名字不是这个用户的某个服务器工具时返回 null（由调用方答「未暴露」）。
    /// </summary>
    private async Task<CallToolResult?> RelayToUserServerAsync(
        string name,
        IDictionary<string, JsonElement>? arguments,
        McpCallerScope caller,
        CancellationToken cancellationToken)
    {
        // 平台自己的工具名（自定义工具、Agent 工具）是保留的：同名的用户工具既不列出也不转发
        var toolSet = await GetToolSetAsync(caller.TenantId, cancellationToken);
        if (_customTools.ContainsKey(name) || toolSet.AgentToolNames.ContainsKey(name))
        {
            return null;
        }

        var target = (await ListUserToolsAsync(caller, cancellationToken))
            .Where(t => string.Equals(t.Name, name, StringComparison.Ordinal))
            .Select(t => (UserRelayTool?)t)
            .FirstOrDefault();
        if (target is null)
        {
            return null;
        }

        var userId = caller.CreatorId!.Value;
        var accessor = _serviceProvider.GetService<IAgentExecutionContextAccessor>();

        return await ExecuteWithGuardsAsync(name, agentId: null, async ct =>
        {
            // 审批 / 权限规则按人匹配取的是当前运行请求里的用户：这里没有 agent 运行上下文，补一份最小的
            var previous = accessor?.CurrentRequest;
            if (accessor is not null)
            {
                accessor.CurrentRequest = new AgentRunRequest { UserId = userId, AgentId = caller.AgentId };
            }

            try
            {
                var functionArguments = new AIFunctionArguments(arguments is null
                    ? new Dictionary<string, object?>()
                    : arguments.ToDictionary(p => p.Key, p => (object?)p.Value));
                var result = await target.Value.Function.InvokeAsync(functionArguments, ct);
                return ToCallToolResult(result);
            }
            finally
            {
                if (accessor is not null)
                {
                    accessor.CurrentRequest = previous;
                }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 把 AI 函数的返回值还原成 MCP 结果：SDK 工具返回的是序列化的 <see cref="CallToolResult"/>（原样透传，保留内容块与 IsError）；
    /// 包装器（审批拒绝）返回的是字符串；其余按文本。
    /// </summary>
    /// <summary>文本结果。审批包装器拒绝时返回以这个前缀开头的字符串而不是抛异常，据此标成错误。</summary>
    private static CallToolResult TextResult(string text)
        => CreateTextResult(text, isError: text.StartsWith("Tool call rejected", StringComparison.Ordinal));

    private static CallToolResult ToCallToolResult(object? result)
    {
        switch (result)
        {
            case null:
                return CreateTextResult(string.Empty, isError: false);
            case CallToolResult callToolResult:
                return callToolResult;
            case JsonElement element:
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("content", out _))
                {
                    try
                    {
                        if (element.Deserialize<CallToolResult>() is { } parsed)
                        {
                            return parsed;
                        }
                    }
                    catch (JsonException)
                    {
                        // 不是 CallToolResult 的形状：按文本处理
                    }
                }

                return element.ValueKind == JsonValueKind.String
                    ? TextResult(element.GetString() ?? string.Empty)
                    : CreateTextResult(element.GetRawText(), isError: false);
            case string text:
                return TextResult(text);
            default:
                return CreateTextResult(JsonSerializer.Serialize(result), isError: false);
        }
    }
}
