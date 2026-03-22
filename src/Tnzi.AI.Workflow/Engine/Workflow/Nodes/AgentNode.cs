namespace Tnzi.AI.Engine.Workflow.Nodes;

/// <summary>
/// Agent 节点 — 通过 IAgentFactory 创建 AgentExecutor 执行 LLM 调用
/// </summary>
/// <remarks>
/// 配置项：
/// - agentId: Agent ID（可选，用于从数据库加载 Agent 定义）
/// - 步骤本身的 Provider/Model/Instructions 优先级高于 Agent 定义
/// </remarks>
public class AgentNode : IWorkflowNode
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AgentNode> _logger;

    public string NodeType => WorkflowNodeTypes.Agent;

    public AgentNode(IServiceProvider serviceProvider, ILogger<AgentNode> logger)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
    }

    public async Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Step;
        var state = context.State;

        // 创建作用域以避免使用根 ServiceProvider 解析 Scoped 服务
        using var scope = _serviceProvider.CreateScope();

        var executor = await WorkflowNodeHelper.CreateAgentExecutorAsync(
            agentId: step.AgentId,
            scope: scope,
            provider: step.Provider,
            model: step.Model,
            instructions: step.Instructions,
            name: step.StepId ?? "agent-node",
            cancellationToken: cancellationToken);

        // 构建输入
        var stepInput = BuildStepInput(step, state);
        stepInput = state.ResolveTemplate(stepInput);

        var messages = new List<ChatMessage> { new(ChatRole.User, stepInput) };
        var response = await executor.ExecuteAsync(messages, cancellationToken);

        return new WorkflowNodeResult
        {
            Output = response.Text ?? string.Empty,
            Usage = response.Usage,
            IsSuccess = true
        };
    }

    /// <summary>
    /// 构建步骤输入：优先使用依赖步骤输出，否则使用初始输入
    /// </summary>
    private static string BuildStepInput(WorkflowStepDto step, WorkflowState state)
    {
        if (step.DependsOn == null || step.DependsOn.Count == 0)
            return state.InitialInput;

        if (step.DependsOn.Count == 1)
        {
            var output = state.GetOutput(step.DependsOn[0]);
            return output?.Text ?? state.InitialInput;
        }

        var sb = new StringBuilder();
        foreach (var depId in step.DependsOn)
        {
            var output = state.GetOutput(depId);
            if (output != null && !string.IsNullOrEmpty(output.Text))
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine($"[{depId}]");
                sb.AppendLine(output.Text);
            }
        }

        return sb.Length > 0 ? sb.ToString().TrimEnd() : state.InitialInput;
    }
}
