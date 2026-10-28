namespace Tnzi.AI.Workflow.Engine.Nodes;

/// <summary>
/// 审查节点 - 注入上游输出 + 审查提示 → LLM 返回结构化 verdict（accept/rework/reject）
/// </summary>
/// <remarks>
/// 配置项：
/// - reviewerAgentId: 审查者 Agent ID（可选；缺失时用步骤的 AgentId）
/// - verdictOptions: 逗号分隔的 verdict 选项（默认 "accept,rework,reject"）
/// </remarks>
public class ReviewNode : IWorkflowNode
{
    private readonly IWorkflowNodeServiceContext _nodeContext;
    private readonly ILogger<ReviewNode> _logger;

    public string NodeType => WorkflowNodeTypes.Review;

    public ReviewNode(IWorkflowNodeServiceContext nodeContext, ILogger<ReviewNode> logger)
    {
        _nodeContext = Check.NotNull(nodeContext);
        _logger = Check.NotNull(logger);
    }

    public async Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Step;
        var state = context.State;
        var config = step.Configuration ?? new Dictionary<string, string>();

        // 解析 verdict 选项
        var verdictOptions = config.TryGetValue("verdictOptions", out var vo) && !string.IsNullOrWhiteSpace(vo)
            ? vo.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : new[] { "accept", "rework", "reject" };

        // 收集上游输出（数据，不做模板替换）
        var upstreamText = WorkflowNodeHelper.CollectInput(context);

        // 构建审查提示：只替换作者写的指令。此前替换的是拼好的整段提示，被审内容里的 {{...}} 也会被换掉
        var reviewPrompt = BuildReviewPrompt(WorkflowNodeHelper.ResolveInstructions(step, state), upstreamText, verdictOptions);

        var reviewerAgentId = WorkflowNodeHelper.ResolveNodeAgentId(step, config, "reviewerAgentId");

        // 系统提示词：选了审查 Agent 用它自己的，否则用节点缺省；verdict JSON 格式的要求在用户消息里
        var executor = await WorkflowNodeHelper.CreateAgentExecutorAsync(
            agentId: reviewerAgentId,
            serviceContext: _nodeContext,
            provider: step.Provider,
            model: step.Model,
            instructions: null,
            name: step.StepId ?? "review-node",
            cancellationToken: cancellationToken,
            fallbackInstructions: "You are a reviewer. Always respond with a JSON object containing 'verdict' and 'feedback' fields.");

        var messages = new List<ChatMessage> { new(ChatRole.User, reviewPrompt) };
        var response = await executor.ExecuteAsync(messages, cancellationToken);

        // 解析 verdict
        var (verdict, feedback) = ParseVerdict(response.Text ?? string.Empty, verdictOptions);

        _logger.LogDebug("Review node '{StepId}' verdict: {Verdict}", step.StepId, verdict);

        return new WorkflowNodeResult
        {
            Output = new WorkflowStepOutput
            {
                Text = feedback,
                Metadata = new Dictionary<string, string>
                {
                    ["verdict"] = verdict,
                    ["feedback"] = feedback
                }
            },
            Usage = response.Usage,
            IsSuccess = true,
            RouteTo = verdict // 用于条件边路由
        };
    }

    /// <summary>
    /// 构建审查提示
    /// </summary>
    private static string BuildReviewPrompt(string? customInstructions, string upstreamText, string[] verdictOptions)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(customInstructions))
        {
            sb.AppendLine(customInstructions);
            sb.AppendLine();
        }

        sb.AppendLine("Please review the following content and provide your verdict.");
        sb.AppendLine($"Your verdict MUST be one of: {string.Join(", ", verdictOptions)}");
        sb.AppendLine();
        sb.AppendLine("Respond with a JSON object: {\"verdict\": \"...\", \"feedback\": \"...\"}");
        sb.AppendLine();
        sb.AppendLine("--- Content to review ---");
        sb.AppendLine(upstreamText);

        return sb.ToString();
    }

    /// <summary>
    /// 解析 LLM 返回的 verdict JSON
    /// </summary>
    private static (string verdict, string feedback) ParseVerdict(string responseText, string[] verdictOptions)
    {
        // 尝试解析 JSON
        try
        {
            var json = responseText.Trim();

            // 剥离 markdown 代码围栏
            if (json.StartsWith("```"))
            {
                var firstNewline = json.IndexOf('\n');
                var lastFence = json.LastIndexOf("```");
                if (firstNewline > 0 && lastFence > firstNewline)
                {
                    json = json[(firstNewline + 1)..lastFence].Trim();
                }
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var verdict = root.TryGetProperty("verdict", out var v) ? v.GetString() ?? "reject" : "reject";
            var feedback = root.TryGetProperty("feedback", out var f) ? f.GetString() ?? responseText : responseText;

            // 验证 verdict 是否在有效选项中
            if (verdictOptions.Any(o => string.Equals(o, verdict, StringComparison.OrdinalIgnoreCase)))
                return (verdict.ToLowerInvariant(), feedback);

            return ("reject", feedback);
        }
        catch
        {
            // JSON 解析失败，尝试从文本中提取 verdict
            var lowerText = responseText.ToLowerInvariant();
            foreach (var option in verdictOptions)
            {
                if (lowerText.Contains(option.ToLowerInvariant()))
                    return (option.ToLowerInvariant(), responseText);
            }

            return ("reject", responseText);
        }
    }
}
