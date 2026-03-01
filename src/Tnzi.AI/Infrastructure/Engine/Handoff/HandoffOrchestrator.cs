
namespace Tnzi.AI.Infrastructure.Engine.Handoff;

/// <summary>
/// Handoff 编排器 — 管理 Agent 间的对话转接
/// </summary>
/// <remarks>
/// <para>
/// 实现 Agent 转接模式（类似 OpenAI Agents SDK 的 Handoff）：
/// 当一个 Agent 通过调用 handoff_to_agent 工具决定将对话转交给另一个 Agent 时，
/// 编排器自动切换到目标 Agent 继续对话。
/// </para>
/// <para>
/// 使用方式：
/// 1. 注册多个 Agent 及其转接目标
/// 2. 指定入口 Agent
/// 3. 调用 RunAsync，编排器自动处理转接链
/// </para>
/// </remarks>
public class HandoffOrchestrator
{
    private readonly Dictionary<string, AgentExecutor> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _handoffTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<HandoffOrchestrator> _logger;

    /// <summary>
    /// 最大转接次数（防止无限循环）
    /// </summary>
    public int MaxHandoffs { get; set; } = 10;

    public HandoffOrchestrator(ILogger<HandoffOrchestrator> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 注册 Agent 及其可转接的目标 Agent 列表
    /// </summary>
    public HandoffOrchestrator AddAgent(AgentExecutor agent, IEnumerable<string>? handoffTargets = null)
    {
        Check.NotNull(agent);
        _agents[agent.Name] = agent;
        if (handoffTargets != null)
        {
            _handoffTargets[agent.Name] = handoffTargets.ToList();
        }
        return this;
    }

    /// <summary>
    /// 从指定的入口 Agent 开始运行，自动处理转接
    /// </summary>
    /// <param name="entryAgentName">入口 Agent 名称</param>
    /// <param name="input">用户输入</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>最终的执行结果（含转接路径信息）</returns>
    public async Task<HandoffResult> RunAsync(string entryAgentName, string input, CancellationToken ct = default)
    {
        Check.NotNullOrEmpty(entryAgentName);
        Check.NotNullOrEmpty(input);

        if (!_agents.TryGetValue(entryAgentName, out var currentAgent))
        {
            throw new InvalidOperationException($"Entry agent '{entryAgentName}' not found");
        }

        var handoffPath = new List<string> { entryAgentName };
        var messages = new List<ChatMessage> { new(ChatRole.User, input) };

        for (var handoffCount = 0; handoffCount < MaxHandoffs; handoffCount++)
        {
            // 为当前 Agent 注入 handoff 工具
            var agentWithHandoff = InjectHandoffTool(currentAgent);
            var response = await agentWithHandoff.ExecuteAsync(messages, ct);

            // 检查是否有 handoff 调用
            var handoffTarget = ExtractHandoffTarget(response);
            if (handoffTarget == null)
            {
                // 没有转接，当前 Agent 完成了对话
                _logger.LogDebug("Handoff completed at agent '{AgentName}' after {HandoffCount} handoffs", currentAgent.Name, handoffCount);
                return new HandoffResult
                {
                    FinalResponse = response,
                    HandoffPath = handoffPath,
                    FinalAgentName = currentAgent.Name
                };
            }

            // 验证目标 Agent 是否在允许的转接列表中
            if (_handoffTargets.TryGetValue(currentAgent.Name, out var allowedTargets)
                && !allowedTargets.Contains(handoffTarget, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Agent '{Source}' attempted to handoff to '{Target}' which is not in allowed targets", currentAgent.Name, handoffTarget);
                return new HandoffResult
                {
                    FinalResponse = response,
                    HandoffPath = handoffPath,
                    FinalAgentName = currentAgent.Name
                };
            }

            if (!_agents.TryGetValue(handoffTarget, out var targetAgent))
            {
                _logger.LogWarning("Handoff target agent '{Target}' not found", handoffTarget);
                return new HandoffResult
                {
                    FinalResponse = response,
                    HandoffPath = handoffPath,
                    FinalAgentName = currentAgent.Name
                };
            }

            _logger.LogDebug("Handoff from '{Source}' to '{Target}'", currentAgent.Name, handoffTarget);
            handoffPath.Add(handoffTarget);
            currentAgent = targetAgent;

            // 将前一个 Agent 的最终输出作为新 Agent 的输入上下文
            var contextMessage = response.Text ?? string.Empty;
            messages =
            [
                new(ChatRole.User, input),
                new(ChatRole.System, $"[Handoff context from {handoffPath[^2]}]: {contextMessage}")
            ];
        }

        _logger.LogWarning("Max handoffs ({MaxHandoffs}) reached", MaxHandoffs);
        return new HandoffResult
        {
            FinalResponse = new AgentResponse { Text = "Max handoff limit reached" },
            HandoffPath = handoffPath,
            FinalAgentName = currentAgent.Name
        };
    }

    /// <summary>
    /// 为 Agent 注入 handoff_to_agent 工具
    /// </summary>
    private AgentExecutor InjectHandoffTool(AgentExecutor agent)
    {
        // 获取当前 Agent 可转接的目标列表
        _handoffTargets.TryGetValue(agent.Name, out var targets);
        var targetDescription = targets != null && targets.Count > 0
            ? $"Available agents to hand off to: {string.Join(", ", targets)}"
            : "Hand off the conversation to another agent by specifying the target agent name.";

        var handoffTool = AIFunctionFactory.Create(
            (string targetAgentName, string? reason) => $"HANDOFF:{targetAgentName}",
            new AIFunctionFactoryOptions
            {
                Name = "handoff_to_agent",
                Description = $"Transfer this conversation to another agent that is better suited to handle the request. {targetDescription}",
            });

        // 创建包含原有工具 + handoff 工具的新 AgentExecutor
        return agent.WithAdditionalTools([handoffTool]);
    }

    /// <summary>
    /// 从 Agent 响应中提取 handoff 目标
    /// </summary>
    private static string? ExtractHandoffTarget(AgentResponse response)
    {
        if (response.Text == null) return null;

        // 检查工具调用结果中是否有 HANDOFF: 前缀
        foreach (var msg in response.Messages ?? [])
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionResultContent frc
                    && frc.Result is string resultText
                    && resultText.StartsWith("HANDOFF:", StringComparison.OrdinalIgnoreCase))
                {
                    return resultText["HANDOFF:".Length..].Trim();
                }
            }
        }

        return null;
    }
}

/// <summary>
/// Handoff 执行结果
/// </summary>
public class HandoffResult
{
    /// <summary>最终 Agent 的响应</summary>
    public AgentResponse FinalResponse { get; set; } = null!;
    /// <summary>转接路径（Agent 名称列表）</summary>
    public List<string> HandoffPath { get; set; } = [];
    /// <summary>最终处理对话的 Agent 名称</summary>
    public string FinalAgentName { get; set; } = string.Empty;
}
