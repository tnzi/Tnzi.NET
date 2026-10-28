namespace Tnzi.AI.Engine.AgentAsTools;

/// <summary>
/// AgentAsTools 执行策略 - 将子 Agent 作为工具注入父 Agent，由 LLM 决定调用
/// 支持单子 Agent 超时 + SSE progress events
/// </summary>
/// <remarks>
/// 子 Agent 一个接一个执行：生成的 <c>call_*</c> 工具不在工具注册表里，执行器按未知工具失败关闭、顺序执行
/// （<see cref="AgentExecutor.CanExecuteConcurrently"/>）。并行需要每个子 Agent 各用自己的 DI 作用域与属性包，
/// 那是设计改动，当前不提供，所以这里也没有并发上限可配。
/// </remarks>
public partial class AgentAsToolsExecutionStrategy : IExecutionStrategy
{
    private readonly AgentAsToolsConfiguration _config;

    public AgentAsToolsExecutionStrategy(AgentAsToolsConfiguration config)
    {
        _config = Check.NotNull(config);
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

        var anyChildInvoked = false;
        await foreach (var chunk in parentWithTools.ExecuteStreamingAsync(messages, ct).WithCancellation(ct))
        {
            // 子 Agent 在父 Agent 的工具调用里跑完，它们的用量必须先于父 Agent 的下一个 chunk 发出：
            // 消费方在第一个终止 chunk 上就收尾（SSE 终止事件、配额结算），排在它后面的用量没人再看
            anyChildInvoked |= !childInvocations.IsEmpty;
            if (DrainChildUsage(childInvocations) is { } childUsage)
            {
                yield return new AgentStreamChunk { Usage = childUsage };
            }

            yield return chunk;
        }

        anyChildInvoked |= !childInvocations.IsEmpty;
        if (DrainChildUsage(childInvocations) is { } remainingUsage)
        {
            yield return new AgentStreamChunk { Usage = remainingUsage };
        }

        if (anyChildInvoked)
        {
            yield return new AgentStreamChunk { FinishReason = FinishReasons.AgentAsToolsComplete };
        }
    }

    /// <summary>取出队列里已完成子 Agent 的用量并相加；没有可报的用量时返回 null。</summary>
    private static TokenUsageDto? DrainChildUsage(ConcurrentQueue<(string Name, int Input, int Output)> invocations)
    {
        int input = 0, output = 0;
        while (invocations.TryDequeue(out var invocation))
        {
            input += invocation.Input;
            output += invocation.Output;
        }

        return ExecutionStrategyAgentLoader.BuildUsage(input, output);
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
            async (string task, CancellationToken invocationToken) =>
            {
                // 调用令牌由执行器给（含工具超时与外部取消），策略令牌是整次运行的；两者任一取消都要停下子 Agent。
                // 此前 lambda 不收调用令牌，工具超时后子 Agent 在后台照跑到 SubAgentTimeout，结果却已被丢弃。
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, invocationToken);

                // 子 Agent 自带上限（SubAgentTimeout，默认 15 分钟），不占父 Agent 这一次工具调用的超时预算：
                // 否则子 Agent 永远跑不过工具超时，SubAgentTimeout 形同虚设
                using var suspended = ToolCallTimeout.Suspend();

                return await ExecuteChildWithTimeoutAsync(
                    childName, childAgentId, task, context, invocations, forwarder, linked.Token);
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
        // 子 Agent 在本方法的执行流里拿一份自己的属性包副本，再打子 Agent 标记。运行的属性包是整次运行共享的
        // 同一个实例（AgentRuntime 进入运行时就建好），直接往里写 IsSubAgent / SubAgentName，子 Agent 返回后
        // 父 Agent 后续的每一次工具调用都按子 Agent 的身份评估权限；并行的两个 call_* 还会互相改写对方的名字。
        // 本方法是独立的 async 帧，这里换掉的属性包不会传回调用方。
        context.ExecutionContextAccessor?.RestoreProperties(previousProperties);
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
            throw; // Parent cancellation - propagate
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
        => SubAgentContext.Mark(context.ExecutionContextAccessor, context.ServiceProvider, childName);

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

            // 每个 chunk 的 Usage 是一次 LLM 调用的增量（StreamUsage 口径），逐个相加
            if (chunk.Usage != null)
            {
                promptTokens += chunk.Usage.InputTokens;
                completionTokens += chunk.Usage.OutputTokens;
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

    private static string Truncate(string value, int maxLength) => value.Truncate(maxLength);

    [GeneratedRegex(@"[^a-z0-9]")]
    private static partial Regex NonAlphanumericLowerRegex();
}
