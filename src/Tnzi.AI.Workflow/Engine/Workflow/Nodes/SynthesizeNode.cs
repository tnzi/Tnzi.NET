namespace Tnzi.AI.Workflow.Engine.Nodes;

/// <summary>
/// 综合节点 - 收集所有上游输出 → LLM 汇总 → 返回综合结果
/// </summary>
/// <remarks>
/// 配置项：
/// - synthesizerAgentId: 综合器 Agent ID（可选；缺失时用步骤的 AgentId）
/// </remarks>
public class SynthesizeNode : IWorkflowNode
{
    private readonly IWorkflowNodeServiceContext _nodeContext;

    public string NodeType => WorkflowNodeTypes.Synthesize;

    public SynthesizeNode(IWorkflowNodeServiceContext nodeContext)
    {
        _nodeContext = Check.NotNull(nodeContext);
    }

    public async Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Step;
        var state = context.State;
        var config = step.Configuration ?? new Dictionary<string, string>();

        // 收集所有上游输出（数据，不做模板替换）
        var upstreamText = WorkflowNodeHelper.CollectAllUpstreamOutputs(context);

        // 构建综合提示：只替换作者写的指令
        var instructions = WorkflowNodeHelper.ResolveInstructions(step, state);
        var synthesizePrompt = BuildSynthesizePrompt(instructions, upstreamText);

        var synthAgentId = WorkflowNodeHelper.ResolveNodeAgentId(step, config, "synthesizerAgentId");

        // 系统提示词：步骤指令 > 综合 Agent 自己的 > 节点缺省
        var executor = await WorkflowNodeHelper.CreateAgentExecutorAsync(
            agentId: synthAgentId,
            serviceContext: _nodeContext,
            provider: step.Provider,
            model: step.Model,
            instructions: instructions,
            name: step.StepId ?? "synthesize-node",
            cancellationToken: cancellationToken,
            fallbackInstructions: "You are a synthesizer. Combine and summarize the following inputs into a coherent, comprehensive response.");

        var messages = new List<ChatMessage> { new(ChatRole.User, synthesizePrompt) };
        var response = await executor.ExecuteAsync(messages, cancellationToken);

        return new WorkflowNodeResult
        {
            Output = new WorkflowStepOutput
            {
                Text = response.Text ?? string.Empty,
                Metadata = new Dictionary<string, string>
                {
                    ["source_count"] = context.DependencyOutputs.Count.ToString()
                }
            },
            Usage = response.Usage,
            IsSuccess = true
        };
    }

    /// <summary>
    /// 构建综合提示
    /// </summary>
    private static string BuildSynthesizePrompt(string? customInstructions, string upstreamText)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(customInstructions))
        {
            sb.AppendLine(customInstructions);
            sb.AppendLine();
        }

        sb.AppendLine("Please synthesize the following inputs into a coherent summary:");
        sb.AppendLine();
        sb.AppendLine(upstreamText);

        return sb.ToString();
    }
}
