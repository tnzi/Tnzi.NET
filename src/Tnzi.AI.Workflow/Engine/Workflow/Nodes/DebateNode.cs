namespace Tnzi.AI.Engine.Workflow.Nodes;

/// <summary>
/// 辩论节点 — 多 Agent 讨论，复用 GroupChatOrchestrator 核心逻辑
/// </summary>
/// <remarks>
/// 配置项：
/// - debateAgentIds: JSON 数组，Agent ID 列表，如 ["guid1", "guid2", "guid3"]
/// - maxRounds: 最大讨论轮次（默认 5）
/// </remarks>
public class DebateNode : IWorkflowNode
{
    private readonly IWorkflowNodeServiceContext _nodeContext;
    private readonly ILogger<DebateNode> _logger;

    public string NodeType => WorkflowNodeTypes.Debate;

    public DebateNode(IWorkflowNodeServiceContext nodeContext, ILogger<DebateNode> logger)
    {
        _nodeContext = Check.NotNull(nodeContext);
        _logger = Check.NotNull(logger);
    }

    public async Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Step;
        var state = context.State;
        var config = step.Configuration ?? new Dictionary<string, string>();

        // 解析辩论 Agent ID 列表
        var agentIds = ParseAgentIds(config);
        if (agentIds.Count < 2)
        {
            return new WorkflowNodeResult
            {
                Output = "Debate requires at least 2 agents",
                IsSuccess = false,
                Error = "Debate node requires 'debateAgentIds' configuration with at least 2 agent IDs"
            };
        }

        // 解析最大轮次
        var maxRounds = config.TryGetValue("maxRounds", out var mrStr) && int.TryParse(mrStr, out var mr) ? mr : 5;

        // 收集输入作为讨论主题
        var topic = WorkflowNodeHelper.CollectInput(context);
        topic = state.ResolveTemplate(topic);

        var orchestrator = new GroupChatOrchestrator(_logger)
        {
            Options = new GroupChatOptions
            {
                MaxRounds = maxRounds,
                SelectionStrategy = GroupChatSelectionStrategy.RoundRobin
            }
        };

        foreach (var agentId in agentIds)
        {
            var agentConfig = new WorkflowNodeHelper.RefHolder
            {
                Provider = step.Provider,
                Model = step.Model,
                AgentName = $"debate-agent-{agentId:N}"
            };

            await WorkflowNodeHelper.ResolveAgentConfigAsync(agentId, _nodeContext, agentConfig, cancellationToken);

            var executor = await _nodeContext.AgentFactory.CreateAgentAsync(
                providerName: agentConfig.Provider,
                model: agentConfig.Model,
                instructions: agentConfig.Instructions,
                name: agentConfig.AgentName ?? $"debate-agent-{agentId:N}",
                agentId: agentId,
                ct: cancellationToken);

            orchestrator.AddAgent(executor);
        }

        // 运行 GroupChat
        var result = await orchestrator.RunAsync(topic, cancellationToken);

        _logger.LogDebug("Debate node '{StepId}' completed after {Rounds} rounds", step.StepId, result.TotalRounds);

        return new WorkflowNodeResult
        {
            Output = new WorkflowStepOutput
            {
                Text = result.Output,
                Metadata = new Dictionary<string, string>
                {
                    ["total_rounds"] = result.TotalRounds.ToString(),
                    ["agent_count"] = agentIds.Count.ToString()
                }
            },
            Usage = result.Usage,
            IsSuccess = true
        };
    }

    /// <summary>
    /// 解析辩论 Agent ID 列表
    /// </summary>
    private static List<Guid> ParseAgentIds(Dictionary<string, string> config)
    {
        if (!config.TryGetValue("debateAgentIds", out var idsJson) || string.IsNullOrWhiteSpace(idsJson))
            return [];

        try
        {
            var ids = JsonSerializer.Deserialize<List<string>>(idsJson, TnziJsonDefaults.Options);
            if (ids == null) return [];

            var result = new List<Guid>();
            foreach (var id in ids)
            {
                if (Guid.TryParse(id, out var guid))
                    result.Add(guid);
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

}
