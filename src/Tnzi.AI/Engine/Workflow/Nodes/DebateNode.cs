
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
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DebateNode> _logger;

    public string NodeType => "debate";

    public DebateNode(IServiceProvider serviceProvider, ILogger<DebateNode> logger)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
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
        var topic = CollectInput(context);
        topic = state.ResolveTemplate(topic);

        // 创建作用域以避免使用根 ServiceProvider 解析 Scoped 服务
        using var scope = _serviceProvider.CreateScope();
        var agentFactory = scope.ServiceProvider.GetRequiredService<IAgentFactory>();
        var agentRepo = scope.ServiceProvider.GetService<IRepository<Agent, Guid>>();

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
            string? provider = step.Provider;
            string? model = step.Model;
            string? instructions = null;
            string name = $"debate-agent-{agentId:N}";

            if (agentRepo != null)
            {
                var agent = await agentRepo.GetAsync(agentId, cancellationToken);
                if (agent != null)
                {
                    provider ??= agent.Provider;
                    model ??= agent.Model;
                    instructions = agent.Instructions;
                    name = agent.Name;
                }
            }

            var executor = await agentFactory.CreateAgentAsync(
                providerName: provider,
                model: model,
                instructions: instructions,
                name: name,
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

    /// <summary>
    /// 收集输入
    /// </summary>
    private static string CollectInput(WorkflowNodeContext context)
    {
        if (context.DependencyOutputs.Count == 0)
            return context.State.InitialInput;

        if (context.DependencyOutputs.Count == 1)
            return context.DependencyOutputs.Values.First().Text;

        var sb = new StringBuilder();
        foreach (var (depId, output) in context.DependencyOutputs)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"[{depId}]");
            sb.AppendLine(output.Text);
        }

        return sb.ToString().TrimEnd();
    }
}
