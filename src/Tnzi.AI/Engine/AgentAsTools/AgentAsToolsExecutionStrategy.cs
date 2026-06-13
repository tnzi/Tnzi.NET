namespace Tnzi.AI.Engine.AgentAsTools;

/// <summary>
/// AgentAsTools 执行策略 — 将子 Agent 作为工具注入父 Agent，由 LLM 决定调用
/// 支持 SemaphoreSlim 并发限制 + 单子 Agent 超时 + SSE progress events
/// </summary>
public partial class AgentAsToolsExecutionStrategy : IExecutionStrategy, IDisposable
{
    private readonly AgentAsToolsConfiguration _config;
    private readonly SemaphoreSlim _concurrencyLimiter;

    public AgentAsToolsExecutionStrategy(AgentAsToolsConfiguration config)
    {
        _config = Check.NotNull(config);
        var maxConcurrent = Math.Clamp(config.MaxConcurrentSubAgents, 2, 4);
        _concurrencyLimiter = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        ValidateNoDuplicateToolNames();
    }

    public async Task<ExecutionResult> ExecuteAsync(IAgentExecutor agent, List<ChatMessage> messages, ExecutionStrategyContext context, CancellationToken ct)
    {
        var childInvocations = new ConcurrentQueue<(string Name, int Input, int Output)>();

        var childTools = CreateChildAgentTools(context, childInvocations, ct);
        var parentWithTools = agent.WithAdditionalTools(childTools);

        var response = await parentWithTools.ExecuteAsync(messages, ct);

        return BuildResult(response, agent.Name, childInvocations);
    }

    public async IAsyncEnumerable<AgentStreamChunk> ExecuteStreamingAsync(IAgentExecutor agent, List<ChatMessage> messages, ExecutionStrategyContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        var childInvocations = new ConcurrentQueue<(string Name, int Input, int Output)>();

        var childTools = CreateChildAgentTools(context, childInvocations, ct);
        var parentWithTools = agent.WithAdditionalTools(childTools);

        await foreach (var chunk in parentWithTools.ExecuteStreamingAsync(messages, ct).WithCancellation(ct))
        {
            yield return chunk;
        }

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

    private AITool CreateChildAgentTool(string childName, Guid childAgentId, ExecutionStrategyContext context, ConcurrentQueue<(string, int, int)> invocations, IAgentStreamForwarder? forwarder, CancellationToken ct)
    {
        return AIFunctionFactory.Create(
            async (string task) =>
            {
                // Acquire concurrency slot
                await _concurrencyLimiter.WaitAsync(ct);
                try
                {
                    return await ExecuteChildWithTimeoutAsync(
                        childName, childAgentId, task, context, invocations, forwarder, ct);
                }
                finally
                {
                    _concurrencyLimiter.Release();
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = $"call_{SanitizeName(childName)}",
                Description = $"Call the '{childName}' agent to handle a specific task. Provide a clear task description as input."
            });
    }

    private async Task<string> ExecuteChildWithTimeoutAsync(
        string childName, Guid childAgentId, string task,
        ExecutionStrategyContext context, ConcurrentQueue<(string, int, int)> invocations,
        IAgentStreamForwarder? forwarder, CancellationToken ct)
    {
        var previousProperties = context.ExecutionContextAccessor?.CaptureProperties();
        SetSubAgentContext(context, childName);

        // Emit started event
        context.EmitEvent?.Invoke(new AgentStreamChunk
        {
            EventType = SubAgentEventTypes.Started,
            EventData = new() { ["agent"] = childName, ["task"] = Truncate(task, 200) },
            Mode = StreamMode.Steps
        });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_config.SubAgentTimeout);

        try
        {
            var childAgent = await ExecutionStrategyAgentLoader.ResolveAgentAsync(childAgentId, context, timeoutCts.Token);
            if (childAgent == null)
            {
                EmitFailedEvent(context, childName, "Agent not available");
                return $"Agent '{childName}' is not available.";
            }

            var childMessages = new List<ChatMessage> { new(ChatRole.User, task) };

            string result;
            if (forwarder != null)
            {
                result = await ExecuteChildStreamingAsync(childAgent, childName, childMessages, forwarder, invocations, timeoutCts.Token);
            }
            else
            {
                var response = await childAgent.ExecuteAsync(childMessages, timeoutCts.Token);
                if (response.Usage != null)
                    invocations.Enqueue((childName, response.Usage.InputTokens, response.Usage.OutputTokens));
                result = response.Text ?? string.Empty;
            }

            // Emit completed event
            context.EmitEvent?.Invoke(new AgentStreamChunk
            {
                EventType = SubAgentEventTypes.Completed,
                EventData = new() { ["agent"] = childName, ["result_length"] = result.Length },
                Mode = StreamMode.Steps
            });

            return result;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Sub-agent timeout (not parent cancellation)
            context.Logger.LogWarning("Child agent '{ChildName}' timed out after {Timeout}", childName, _config.SubAgentTimeout);
            context.EmitEvent?.Invoke(new AgentStreamChunk
            {
                EventType = SubAgentEventTypes.TimedOut,
                EventData = new() { ["agent"] = childName, ["timeout_seconds"] = _config.SubAgentTimeout.TotalSeconds },
                Mode = StreamMode.Steps
            });
            return $"Agent '{childName}' timed out after {_config.SubAgentTimeout.TotalSeconds}s.";
        }
        catch (OperationCanceledException)
        {
            throw; // Parent cancellation — propagate
        }
        catch (Exception ex)
        {
            context.Logger.LogWarning(ex, "Child agent '{ChildName}' execution failed", childName);
            EmitFailedEvent(context, childName, ex.Message);
            return $"Agent '{childName}' encountered an error: {ex.Message}";
        }
        finally
        {
            context.ExecutionContextAccessor?.RestoreProperties(previousProperties);
        }
    }

    private static void SetSubAgentContext(ExecutionStrategyContext context, string childName)
    {
        var accessor = context.ExecutionContextAccessor;
        if (accessor == null)
        {
            return;
        }

        // Capture parent session rules before overwriting context
        var permissionEvaluator = context.ServiceProvider.GetService<IToolPermissionEvaluator>();
        if (permissionEvaluator != null)
        {
            var sessionRules = permissionEvaluator.GetSessionRules();
            if (sessionRules.Count > 0)
            {
                accessor.Properties[ContextPropertyKeys.ParentSessionRules] = sessionRules;
            }
        }

        accessor.Properties[ContextPropertyKeys.IsSubAgent] = true;
        accessor.Properties[ContextPropertyKeys.SubAgentName] = childName;
    }

    private static void EmitFailedEvent(ExecutionStrategyContext context, string childName, string error)
    {
        context.EmitEvent?.Invoke(new AgentStreamChunk
        {
            EventType = SubAgentEventTypes.Failed,
            EventData = new() { ["agent"] = childName, ["error"] = Truncate(error, 200) },
            Mode = StreamMode.Steps
        });
    }

    /// <summary>
    /// Execute child agent with streaming, forwarding each delta chunk through the forwarder.
    /// </summary>
    private static async Task<string> ExecuteChildStreamingAsync(
        IAgentExecutor childAgent, string childName, List<ChatMessage> childMessages,
        IAgentStreamForwarder forwarder, ConcurrentQueue<(string, int, int)> invocations, CancellationToken ct)
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
            invocations.Enqueue((childName, promptTokens, completionTokens));

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

    public void Dispose()
    {
        _concurrencyLimiter.Dispose();
    }

    private static string Truncate(string value, int maxLength)
        => value.Length > maxLength ? value[..maxLength] + "..." : value;

    [GeneratedRegex(@"[^a-z0-9]")]
    private static partial Regex NonAlphanumericLowerRegex();
}
