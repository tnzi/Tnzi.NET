namespace Tnzi.AI.Engine.Workflow.Nodes;

/// <summary>
/// 综合节点 — 收集所有上游输出 → LLM 汇总 → 返回综合结果
/// </summary>
/// <remarks>
/// 配置项：
/// - synthesizerAgentId: 综合器 Agent ID（可选）
/// </remarks>
public class SynthesizeNode : IWorkflowNode
{
    private readonly IWorkflowNodeServiceContext _nodeContext;
    private readonly ILogger<SynthesizeNode> _logger;

    public string NodeType => WorkflowNodeTypes.Synthesize;

    public SynthesizeNode(IWorkflowNodeServiceContext nodeContext, ILogger<SynthesizeNode> logger)
    {
        _nodeContext = Check.NotNull(nodeContext);
        _logger = Check.NotNull(logger);
    }

    public async Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Step;
        var state = context.State;
        var config = step.Configuration ?? new Dictionary<string, string>();

        // 收集所有上游输出
        var upstreamText = WorkflowNodeHelper.CollectAllUpstreamOutputs(context);

        // 构建综合提示
        var synthesizePrompt = BuildSynthesizePrompt(step.Instructions, upstreamText);
        synthesizePrompt = state.ResolveTemplate(synthesizePrompt);

        var synthAgentId = WorkflowNodeHelper.ParseAgentIdFromConfig(config, "synthesizerAgentId");

        var executor = await WorkflowNodeHelper.CreateAgentExecutorAsync(
            agentId: synthAgentId,
            serviceContext: _nodeContext,
            provider: step.Provider,
            model: step.Model,
            instructions: step.Instructions
                ?? "You are a synthesizer. Combine and summarize the following inputs into a coherent, comprehensive response.",
            name: step.StepId ?? "synthesize-node",
            cancellationToken: cancellationToken);

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
