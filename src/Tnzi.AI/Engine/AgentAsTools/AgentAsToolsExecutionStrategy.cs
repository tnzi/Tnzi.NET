namespace Tnzi.AI.Engine.AgentAsTools;

/// <summary>
/// AgentAsTools 执行策略 — 将子 Agent 作为工具注入父 Agent，由 LLM 决定调用
/// </summary>
public partial class AgentAsToolsExecutionStrategy : IExecutionStrategy
{
    private readonly AgentAsToolsConfiguration _config;

    public AgentAsToolsExecutionStrategy(AgentAsToolsConfiguration config)
    {
        _config = Check.NotNull(config);
        ValidateNoDuplicateToolNames();
    }

    public async Task<ExecutionResult> ExecuteAsync(AgentExecutor agent, List<ChatMessage> messages, ExecutionStrategyContext context, CancellationToken ct)
    {
        var childInvocations = new ConcurrentQueue<(string Name, int Input, int Output)>();

        var childTools = CreateChildAgentTools(context, childInvocations, ct);
        var parentWithTools = agent.WithAdditionalTools(childTools);

        var response = await parentWithTools.ExecuteAsync(messages, ct);

        return BuildResult(response, agent.Name, childInvocations);
    }

    public async IAsyncEnumerable<AgentStreamChunk> ExecuteStreamingAsync(AgentExecutor agent, List<ChatMessage> messages, ExecutionStrategyContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        var childInvocations = new ConcurrentQueue<(string Name, int Input, int Output)>();

        var childTools = CreateChildAgentTools(context, childInvocations, ct);
        var parentWithTools = agent.WithAdditionalTools(childTools);

        await foreach (var chunk in parentWithTools.ExecuteStreamingAsync(messages, ct).WithCancellation(ct))
        {
            yield return chunk;
        }

        // 发射子 Agent 聚合用量的最终元数据 chunk
        if (!childInvocations.IsEmpty)
        {
            int totalInput = 0, totalOutput = 0;
            while (childInvocations.TryDequeue(out var inv))
            {
                totalInput += inv.Input;
                totalOutput += inv.Output;
            }

            yield return new AgentStreamChunk
            {
                Usage = ExecutionStrategyAgentLoader.BuildUsage(totalInput, totalOutput),
                FinishReason = FinishReasons.AgentAsToolsComplete
            };
        }
    }

    private List<AITool> CreateChildAgentTools(ExecutionStrategyContext context, ConcurrentQueue<(string, int, int)> invocations, CancellationToken ct)
    {
        // 仅当配置启用时解析 forwarder，避免不必要的 DI 解析
        var forwarder = _config.EnableChildStreaming
            ? context.ServiceProvider.GetService<IAgentStreamForwarder>()
            : null;

        var tools = new List<AITool>();
        foreach (var (name, agentId) in _config.Agents)
        {
            tools.Add(CreateChildAgentTool(name, agentId, context, invocations, forwarder, ct));
        }

        return tools;
    }

    private static AITool CreateChildAgentTool(string childName, Guid childAgentId, ExecutionStrategyContext context, ConcurrentQueue<(string, int, int)> invocations, IAgentStreamForwarder? forwarder, CancellationToken ct)
    {
        return AIFunctionFactory.Create(
            async (string task) =>
            {
                try
                {
                    var childAgent = await ExecutionStrategyAgentLoader.ResolveAgentAsync(childAgentId, context, ct);
                    if (childAgent == null)
                        return $"Agent '{childName}' is not available.";

                    var childMessages = new List<ChatMessage> { new(ChatRole.User, task) };

                    // 注册了流式转发器时使用流式执行，实时转发子 Agent 输出
                    if (forwarder != null)
                    {
                        return await ExecuteChildStreamingAsync(childAgent, childName, childMessages, forwarder, invocations, ct);
                    }

                    // 未注册时使用非流式执行（原有行为）
                    var response = await childAgent.ExecuteAsync(childMessages, ct);

                    if (response.Usage != null)
                    {
                        invocations.Enqueue((childName, response.Usage.InputTokens, response.Usage.OutputTokens));
                    }

                    return response.Text ?? string.Empty;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    context.Logger.LogWarning(ex, "Child agent '{ChildName}' execution failed", childName);
                    return $"Agent '{childName}' encountered an error: {ex.Message}";
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = $"call_{SanitizeName(childName)}",
                Description = $"Call the '{childName}' agent to handle a specific task. Provide a clear task description as input."
            });
    }

    /// <summary>
    /// Execute child agent with streaming, forwarding each delta chunk through the forwarder.
    /// </summary>
    private static async Task<string> ExecuteChildStreamingAsync(
        AgentExecutor childAgent,
        string childName,
        List<ChatMessage> childMessages,
        IAgentStreamForwarder forwarder,
        ConcurrentQueue<(string, int, int)> invocations,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        int promptTokens = 0, completionTokens = 0;

        await foreach (var chunk in childAgent.ExecuteStreamingAsync(childMessages, ct))
        {
            if (!string.IsNullOrEmpty(chunk.Text))
            {
                sb.Append(chunk.Text);
                await forwarder.WriteAsync(childName, chunk.Text, ct);
            }

            if (chunk.Usage != null)
            {
                promptTokens = chunk.Usage.InputTokens;
                completionTokens = chunk.Usage.OutputTokens;
            }
        }

        if (promptTokens > 0 || completionTokens > 0)
        {
            invocations.Enqueue((childName, promptTokens, completionTokens));
        }

        return sb.ToString();
    }

    public static string SanitizeName(string name)
    {
        return NonAlphanumericLowerRegex().Replace(name.ToLowerInvariant(), "_").Trim('_');
    }

    private void ValidateNoDuplicateToolNames()
    {
        var seen = new HashSet<string>();
        foreach (var name in _config.Agents.Keys)
        {
            var toolName = $"call_{SanitizeName(name)}";
            if (!seen.Add(toolName))
                throw new InvalidOperationException($"Duplicate tool name '{toolName}' generated from agent name '{name}'. Rename agents to produce unique tool names.");
        }
    }

    private static ExecutionResult BuildResult(AgentResponse response, string parentName, ConcurrentQueue<(string Name, int Input, int Output)> childInvocations)
    {
        var handoffPath = new List<string> { parentName };
        int totalInput = 0, totalOutput = 0;

        if (response.Usage != null)
        {
            totalInput += response.Usage.InputTokens;
            totalOutput += response.Usage.OutputTokens;
        }

        foreach (var (name, input, output) in childInvocations)
        {
            handoffPath.Add(name);
            totalInput += input;
            totalOutput += output;
        }

        return new ExecutionResult
        {
            Response = response,
            HandoffPath = handoffPath,
            FinalAgentName = parentName,
            AggregatedUsage = ExecutionStrategyAgentLoader.BuildUsage(totalInput, totalOutput)
        };
    }

    [GeneratedRegex(@"[^a-z0-9]")]
    private static partial Regex NonAlphanumericLowerRegex();
}
