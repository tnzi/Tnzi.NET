namespace Tnzi.AI.Workflow.Engine.Nodes;

/// <summary>
/// Agent 节点 - 通过 IAgentFactory 创建 AgentExecutor 执行 LLM 调用
/// </summary>
/// <remarks>
/// 配置项：
/// - agentId: Agent ID（可选，用于从数据库加载 Agent 定义）
/// - 步骤本身的 Provider/Model/Instructions 优先级高于 Agent 定义
/// </remarks>
public class AgentNode : IWorkflowNode
{
    private readonly IWorkflowNodeServiceContext _nodeContext;

    public string NodeType => WorkflowNodeTypes.Agent;

    public AgentNode(IWorkflowNodeServiceContext nodeContext)
    {
        _nodeContext = Check.NotNull(nodeContext);
    }

    public async Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Step;
        var state = context.State;

        var executor = await WorkflowNodeHelper.CreateAgentExecutorAsync(
            agentId: step.AgentId,
            serviceContext: _nodeContext,
            provider: step.Provider,
            model: step.Model,
            instructions: WorkflowNodeHelper.ResolveInstructions(step, state),
            name: step.StepId ?? "agent-node",
            cancellationToken: cancellationToken);

        // 构建输入（与引擎 node input summary 共用 WorkflowNodeHelper.BuildStepInput）。
        // 输入是数据，不做模板替换（见 WorkflowNodeHelper.ResolveInstructions）。
        var stepInput = WorkflowNodeHelper.BuildStepInput(step, state);

        var messages = new List<ChatMessage> { new(ChatRole.User, stepInput) };
        var response = await executor.ExecuteAsync(messages, cancellationToken);

        return new WorkflowNodeResult
        {
            Output = response.Text ?? string.Empty,
            Usage = response.Usage,
            IsSuccess = true
        };
    }
}
