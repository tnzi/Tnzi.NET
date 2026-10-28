

namespace Tnzi.AI.Engine.Handoff;

/// <summary>
/// Handoff 执行策略 - 管理 Agent 间的对话转接循环
/// </summary>
public class HandoffExecutionStrategy : IExecutionStrategy
{
    private readonly HandoffConfiguration _config;

    public HandoffExecutionStrategy(HandoffConfiguration config)
    {
        _config = Check.NotNull(config);
    }

    public async Task<ExecutionResult> ExecuteAsync(IAgentExecutor agent, List<ChatMessage> messages, ExecutionStrategyContext context, CancellationToken ct)
    {
        var handoffPath = new List<string> { agent.Name };
        var currentAgent = agent;
        var currentMessages = messages;
        int totalInputTokens = 0, totalOutputTokens = 0;

        // 双向 handoff：追踪上一个 Agent 以便注入回退目标
        string? previousAgentName = null;
        Guid? previousAgentId = null;

        for (var hop = 0; hop < _config.MaxHandoffs; hop++)
        {
            // 首跳时使用 context.StartingAgentId 作为 previousAgentId
            if (hop == 0 && previousAgentId == null)
                previousAgentId = context.StartingAgentId;

            var effectiveTargets = BuildEffectiveTargets(previousAgentName, previousAgentId);
            var agentWithHandoff = InjectHandoffTool(currentAgent, effectiveTargets.Keys);
            var response = await agentWithHandoff.ExecuteAsync(currentMessages, ct);

            // 累加 token
            if (response.Usage != null)
            {
                totalInputTokens += response.Usage.InputTokens;
                totalOutputTokens += response.Usage.OutputTokens;
            }

            var handoffTarget = ExtractHandoffTarget(response);
            if (handoffTarget == null)
            {
                // 无转接，当前 Agent 完成对话
                context.Logger.LogDebug("Handoff completed at agent '{AgentName}' after {HopCount} hops", currentAgent.Name, hop);
                return BuildResult(response, handoffPath, currentAgent.Name, totalInputTokens, totalOutputTokens);
            }

            // 验证目标是否在有效列表中
            if (!effectiveTargets.TryGetValue(handoffTarget, out var targetAgentId))
            {
                context.Logger.LogWarning("Handoff target '{Target}' not in allowed targets", handoffTarget);
                return BuildResult(response, handoffPath, currentAgent.Name, totalInputTokens, totalOutputTokens);
            }

            // 转接过去的 Agent 是模型自己选的，不是这次请求最初面向的那一个，
            // 与子 Agent 同一个风险面，IsSubAgentOnly 的规则必须在这里也生效。
            // 标记必须先于解析：目标的工具列表在解析时一次定型，ToolResolver 此刻读标记决定是否裁掉
            // AI:SubAgent:SubAgentDisallowedTools；先解析后标记，这份名单对模型选出的首个目标一条也不生效。
            SubAgentContext.Mark(context.ExecutionContextAccessor, context.ServiceProvider, handoffTarget);

            // 按 Guid 加载并创建目标 Agent
            var targetAgent = await ExecutionStrategyAgentLoader.ResolveAgentAsync(targetAgentId, context, ct);
            if (targetAgent == null)
            {
                context.Logger.LogWarning("Handoff target agent '{Target}' (ID: {AgentId}) not found or disabled", handoffTarget, targetAgentId);
                return BuildResult(response, handoffPath, currentAgent.Name, totalInputTokens, totalOutputTokens);
            }

            SubAgentContext.Rename(context.ExecutionContextAccessor, targetAgent.Name);
            context.Logger.LogDebug("Handoff from '{Source}' to '{Target}'", currentAgent.Name, handoffTarget);

            // 保存当前 Agent 作为"上一个"（供下一跳回退用）
            previousAgentName = currentAgent.Name;
            previousAgentId = effectiveTargets.TryGetValue(currentAgent.Name, out var currentId)
                ? currentId
                : context.StartingAgentId;

            handoffPath.Add(handoffTarget);
            currentAgent = targetAgent;

            currentMessages = BuildHandoffMessages(messages, handoffPath[^2]);
        }

        context.Logger.LogWarning("Max handoffs ({MaxHandoffs}) reached", _config.MaxHandoffs);
        return BuildResult(
            new AgentResponse { Text = "Max handoff limit reached", FinishReason = FinishReasons.MaxHandoffs },
            handoffPath, currentAgent.Name, totalInputTokens, totalOutputTokens);
    }

    public async IAsyncEnumerable<AgentStreamChunk> ExecuteStreamingAsync(IAgentExecutor agent, List<ChatMessage> messages, ExecutionStrategyContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        // 策略：流式执行（含 handoff 工具），通过闭包捕获 handoff 决策。
        // 工具调用阶段（文本到达之前）缓冲 chunk，检测到 handoff 则丢弃缓冲并转向目标 agent 流式；
        // 未检测到 handoff 则释放缓冲并继续真正流式输出。
        var handoffPath = new List<string> { agent.Name };
        var currentAgent = agent;
        var currentMessages = messages;

        string? previousAgentName = null;
        Guid? previousAgentId = null;

        for (var hop = 0; hop < _config.MaxHandoffs; hop++)
        {
            if (hop == 0 && previousAgentId == null)
                previousAgentId = context.StartingAgentId;

            var effectiveTargets = BuildEffectiveTargets(previousAgentName, previousAgentId);

            string? detectedHandoff = null;
            var agentWithHandoff = InjectHandoffTool(currentAgent, effectiveTargets.Keys, target => detectedHandoff = target);

            // 流式执行：工具调用阶段缓冲；转接工具一触发就决策，否则在第一个文本到达时释放缓冲
            var preTextBuffer = new List<AgentStreamChunk>();
            var handoffTriggered = false;

            await foreach (var chunk in agentWithHandoff.ExecuteStreamingAsync(currentMessages, ct).WithCancellation(ct))
            {
                if (chunk.Text == null && chunk.ReasoningText == null)
                {
                    // 还在工具调用阶段 - 缓冲（不发给用户）
                    preTextBuffer.Add(chunk);

                    // 转接工具在本轮工具执行时就已触发：立刻中止，不让执行器再发起下一次 LLM 调用。
                    // 那次调用的输出只会被丢弃，却照样计费，而且中途中止时量不到它的用量。
                    if (detectedHandoff != null)
                    {
                        handoffTriggered = true;
                        break;
                    }
                    continue;
                }

                // 第一个文本/推理 chunk 到达 - 检查 handoff
                if (detectedHandoff != null)
                {
                    // handoff 已触发 - 中止当前流，转向目标 agent
                    handoffTriggered = true;
                    break;
                }

                // 无 handoff - 释放缓冲的工具调用 chunk，然后真正流式输出
                if (preTextBuffer.Count > 0)
                {
                    foreach (var buffered in preTextBuffer) yield return buffered;
                    preTextBuffer.Clear();
                }

                yield return chunk;
            }

            // 如果流正常结束（没有文本 chunk），也检查一下 handoff
            if (!handoffTriggered && detectedHandoff != null)
            {
                handoffTriggered = true;
            }

            if (!handoffTriggered)
            {
                // 流正常结束，无 handoff - 释放可能剩余的缓冲（如纯工具调用无文本的情况）
                foreach (var buffered in preTextBuffer) yield return buffered;
                yield break;
            }

            // handoff 触发 - 验证目标并循环
            if (!effectiveTargets.TryGetValue(detectedHandoff!, out var targetAgentId))
            {
                context.Logger.LogWarning("Handoff target '{Target}' not in allowed targets", detectedHandoff);
                foreach (var buffered in preTextBuffer) yield return buffered;
                yield break;
            }

            // 同上：流式路径也要先标记再解析，否则同一份工具名单在流式与非流式下答案不一样
            SubAgentContext.Mark(context.ExecutionContextAccessor, context.ServiceProvider, detectedHandoff);
            var targetAgent = await ExecutionStrategyAgentLoader.ResolveAgentAsync(targetAgentId, context, ct);
            if (targetAgent == null)
            {
                context.Logger.LogWarning("Handoff target agent '{Target}' (ID: {AgentId}) not found or disabled", detectedHandoff, targetAgentId);
                foreach (var buffered in preTextBuffer) yield return buffered;
                yield break;
            }

            SubAgentContext.Rename(context.ExecutionContextAccessor, targetAgent.Name);
            context.Logger.LogDebug("Handoff from '{Source}' to '{Target}'", currentAgent.Name, detectedHandoff);

            previousAgentName = currentAgent.Name;
            previousAgentId = effectiveTargets.TryGetValue(currentAgent.Name, out var currentId)
                ? currentId
                : context.StartingAgentId;

            handoffPath.Add(detectedHandoff!);
            currentAgent = targetAgent;

            // 丢弃的是转出方的工具调用 chunk，不是它的用量：那几次 LLM 调用照样计费
            if (StreamUsage.Sum(preTextBuffer) is { } transferringUsage)
            {
                yield return new AgentStreamChunk { Usage = transferringUsage };
            }

            // 发射 Agent 变更事件，通知前端新 Agent 开始
            yield return new AgentStreamChunk { AgentName = detectedHandoff! };

            // 下一轮循环从目标 agent 流式执行，与非流式同一份消息
            currentMessages = BuildHandoffMessages(messages, handoffPath[^2]);
        }

        // Max handoffs reached - yield error chunk
        yield return new AgentStreamChunk { Text = "Max handoff limit reached", FinishReason = FinishReasons.MaxHandoffs };
    }

    /// <summary>
    /// 交给下一跳 Agent 的消息：本次运行的对话原样带过去（历史、用户画像、检索到的上下文、本轮用户输入），
    /// 去掉转出方 Agent 的人格块，末尾附一条转接说明。流式与非流式共用。
    /// </summary>
    /// <remarks>
    /// 此前取 <c>messages[0]</c> 当「原始用户消息」，而上下文注入把人格块与用户画像插在第 0 位、历史也插在前面：
    /// 只有无人格、无画像、无历史的新线程上它才是用户消息。其余时候目标 Agent 收到的是转出方的人格
    /// （以转出方的身份作答），或是几周前那条对话的第一句，最近的上下文一条不剩。
    /// </remarks>
    internal static List<ChatMessage> BuildHandoffMessages(List<ChatMessage> messages, string sourceAgentName)
    {
        var userQuestion = ExecutionStrategyAgentLoader.GetLatestUserQuestion(messages);
        var handoffMessages = messages.Where(m => !ContextInjectionMiddleware.IsPersonaBlock(m)).ToList();
        handoffMessages.Add(new ChatMessage(ChatRole.System,
            $"[Handoff from {sourceAgentName}]: The user asked: \"{userQuestion}\". Answer this question directly using your tools."));
        return handoffMessages;
    }

    /// <summary>
    /// 构建有效的目标列表 - 基础 Targets + 自动注入来源 Agent
    /// </summary>
    private Dictionary<string, Guid> BuildEffectiveTargets(string? previousAgentName, Guid? previousAgentId)
    {
        if (!_config.AllowReturnToSource || previousAgentName == null || previousAgentId == null)
            return _config.Targets;

        if (_config.Targets.ContainsKey(previousAgentName))
            return _config.Targets;

        var effective = new Dictionary<string, Guid>(_config.Targets)
        {
            [previousAgentName] = previousAgentId.Value
        };
        return effective;
    }

    /// <summary>
    /// 为 Agent 注入 handoff_to_agent 工具
    /// </summary>
    /// <param name="agent">要注入工具的 Agent</param>
    /// <param name="availableTargets">可用的转接目标名称</param>
    /// <param name="onHandoff">工具被调用时的回调（流式模式用于捕获 handoff 决策）</param>
    internal static IAgentExecutor InjectHandoffTool(IAgentExecutor agent, IEnumerable<string> availableTargets, Action<string>? onHandoff = null)
    {
        var targetList = availableTargets.ToList();
        var targetDescription = targetList.Count > 0
            ? $"Available agents to hand off to: {string.Join(", ", targetList)}"
            : "Hand off the conversation to another agent by specifying the target agent name.";

        var handoffTool = AIFunctionFactory.Create(
            (string targetAgentName, string? reason) =>
            {
                onHandoff?.Invoke(targetAgentName);
                return $"HANDOFF:{targetAgentName}";
            },
            new AIFunctionFactoryOptions
            {
                Name = "handoff_to_agent",
                Description = $"Transfer this conversation to another agent that is better suited to handle the request. {targetDescription}",
            });

        return agent.WithAdditionalTools([handoffTool]);
    }

    /// <summary>
    /// 从 Agent 响应中提取 handoff 目标
    /// </summary>
    internal static string? ExtractHandoffTarget(AgentResponse response)
    {
        foreach (var msg in response.Messages ?? [])
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent fcc
                    && string.Equals(fcc.Name, "handoff_to_agent", StringComparison.OrdinalIgnoreCase)
                    && fcc.Arguments?.TryGetValue("targetAgentName", out var targetArg) == true)
                {
                    var targetName = targetArg?.ToString();
                    if (!string.IsNullOrWhiteSpace(targetName))
                    {
                        return targetName;
                    }
                }

                if (content is FunctionResultContent frc
                    && frc.Result is string resultText
                    && resultText.StartsWith("HANDOFF:", StringComparison.OrdinalIgnoreCase))
                {
                    var target = resultText["HANDOFF:".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(target))
                        return target;
                }
            }
        }

        return null;
    }

    private static ExecutionResult BuildResult(AgentResponse response, List<string> handoffPath, string finalAgentName, int inputTokens, int outputTokens)
    {
        return new ExecutionResult
        {
            Response = response,
            HandoffPath = handoffPath,
            FinalAgentName = finalAgentName,
            AggregatedUsage = ExecutionStrategyAgentLoader.BuildUsage(inputTokens, outputTokens)
        };
    }
}
