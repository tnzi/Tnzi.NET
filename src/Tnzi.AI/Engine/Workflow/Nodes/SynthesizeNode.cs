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
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SynthesizeNode> _logger;

    public string NodeType => "synthesize";

    public SynthesizeNode(IServiceProvider serviceProvider, ILogger<SynthesizeNode> logger)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
    }

    public async Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Step;
        var state = context.State;
        var config = step.Configuration ?? new Dictionary<string, string>();

        // 收集所有上游输出
        var upstreamText = CollectAllUpstreamOutputs(context);

        // 构建综合提示
        var synthesizePrompt = BuildSynthesizePrompt(step.Instructions, upstreamText);
        synthesizePrompt = state.ResolveTemplate(synthesizePrompt);

        // 创建作用域以避免使用根 ServiceProvider 解析 Scoped 服务
        using var scope = _serviceProvider.CreateScope();
        var agentFactory = scope.ServiceProvider.GetRequiredService<IAgentFactory>();

        string? provider = step.Provider;
        string? model = step.Model;

        if (config.TryGetValue("synthesizerAgentId", out var synthIdStr)
            && Guid.TryParse(synthIdStr, out var synthAgentId))
        {
            var agentRepo = scope.ServiceProvider.GetService<IRepository<Agent, Guid>>();
            if (agentRepo != null)
            {
                var synthAgent = await agentRepo.GetAsync(synthAgentId, cancellationToken);
                if (synthAgent != null)
                {
                    provider ??= synthAgent.Provider;
                    model ??= synthAgent.Model;
                }
            }
        }

        var instructions = step.Instructions
            ?? "You are a synthesizer. Combine and summarize the following inputs into a coherent, comprehensive response.";

        var executor = await agentFactory.CreateAgentAsync(
            providerName: provider,
            model: model,
            instructions: instructions,
            name: step.StepId ?? "synthesize-node",
            ct: cancellationToken);

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
    /// 收集所有上游输出
    /// </summary>
    private static string CollectAllUpstreamOutputs(WorkflowNodeContext context)
    {
        if (context.DependencyOutputs.Count == 0)
            return context.State.InitialInput;

        var sb = new StringBuilder();
        foreach (var (depId, output) in context.DependencyOutputs)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"[{depId}]");
            sb.AppendLine(output.Text);
        }

        return sb.ToString().TrimEnd();
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
