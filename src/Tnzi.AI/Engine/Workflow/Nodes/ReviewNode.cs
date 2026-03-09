namespace Tnzi.AI.Engine.Workflow.Nodes;

/// <summary>
/// 审查节点 — 注入上游输出 + 审查提示 → LLM 返回结构化 verdict（accept/rework/reject）
/// </summary>
/// <remarks>
/// 配置项：
/// - reviewerAgentId: 审查者 Agent ID（可选）
/// - verdictOptions: 逗号分隔的 verdict 选项（默认 "accept,rework,reject"）
/// </remarks>
public class ReviewNode : IWorkflowNode
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ReviewNode> _logger;

    public string NodeType => "review";

    public ReviewNode(IServiceProvider serviceProvider, ILogger<ReviewNode> logger)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
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

        // 收集上游输出
        var upstreamText = CollectUpstreamOutputs(context);

        // 构建审查提示
        var reviewPrompt = BuildReviewPrompt(step.Instructions, upstreamText, verdictOptions);
        reviewPrompt = state.ResolveTemplate(reviewPrompt);

        // 创建作用域以避免使用根 ServiceProvider 解析 Scoped 服务
        using var scope = _serviceProvider.CreateScope();
        var agentFactory = scope.ServiceProvider.GetRequiredService<IAgentFactory>();

        string? provider = step.Provider;
        string? model = step.Model;

        if (config.TryGetValue("reviewerAgentId", out var reviewerIdStr)
            && Guid.TryParse(reviewerIdStr, out var reviewerAgentId))
        {
            var agentRepo = scope.ServiceProvider.GetService<IRepository<Agent, Guid>>();
            if (agentRepo != null)
            {
                var reviewerAgent = await agentRepo.GetAsync(reviewerAgentId, cancellationToken);
                if (reviewerAgent != null)
                {
                    provider ??= reviewerAgent.Provider;
                    model ??= reviewerAgent.Model;
                }
            }
        }

        var executor = await agentFactory.CreateAgentAsync(
            providerName: provider,
            model: model,
            instructions: "You are a reviewer. Always respond with a JSON object containing 'verdict' and 'feedback' fields.",
            name: step.StepId ?? "review-node",
            ct: cancellationToken);

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
    /// 收集上游依赖的输出文本
    /// </summary>
    private static string CollectUpstreamOutputs(WorkflowNodeContext context)
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
