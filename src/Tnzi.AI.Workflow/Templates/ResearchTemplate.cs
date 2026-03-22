
namespace Tnzi.AI.Templates;

/// <summary>
/// 研究模板。
/// Search → Analysis → Review → Synthesize
/// </summary>
/// <remarks>
/// 典型场景：需要先检索信息，然后分析、审查质量，最终汇总成报告的研究任务。
/// 支持可选的多轮搜索（多个 Search Agent 并行）。
/// </remarks>
[ExperimentalApi(Reason = "Workflow templates are in preview")]
public static class ResearchTemplate
{
    /// <summary>
    /// 构建研究工作流图
    /// </summary>
    public static WorkflowGraph Build(ResearchTemplateOptions options)
    {
        Check.NotNull(options);

        var builder = WorkflowBuilder.Create();

        // 1. Search 阶段 — 单个或多个 Search Agent 并行
        if (options.SearchAgentIds is { Count: > 1 })
        {
            // 多 Search Agent 并行
            var workers = options.SearchAgentIds.Select((id, i) => new Dictionary<string, object>
            {
                ["stepId"] = $"search-{i + 1}",
                ["agentId"] = id
            });
            var workersJson = JsonSerializer.Serialize(workers, TnziJsonDefaults.Options);

            builder.AddParallelStep("search")
                .WithConfiguration("workers", workersJson)
                .WithInstructions(options.SearchInstructions ?? "Search and gather relevant information for: {{input}}");
        }
        else
        {
            // 单 Search Agent
            var searchAgentId = options.SearchAgentIds?.FirstOrDefault() ?? options.AnalysisAgentId;
            builder.AddStep("search", agentId: searchAgentId)
                .WithInstructions(options.SearchInstructions ?? "Search and gather relevant information for: {{input}}");
        }

        // 2. Analysis 节点 — 分析搜索结果
        builder.AddStep("analysis", agentId: options.AnalysisAgentId)
            .DependsOn("search")
            .WithInstructions(options.AnalysisInstructions
                ?? "Analyze the following search results and extract key insights:\n\n{{search}}");

        // 3. Review 节点（可选）— 审查分析质量
        if (options.ReviewerAgentId.HasValue)
        {
            builder.AddReviewStep("review", agentId: options.ReviewerAgentId.Value)
                .DependsOn("analysis");

            // 4. Synthesize 节点 — 汇总最终报告
            builder.AddSynthesizeStep("synthesize", agentId: options.SynthesizerAgentId)
                .DependsOn("review")
                .WithInstructions(options.SynthesizeInstructions
                    ?? "Based on the analysis and review, produce a comprehensive research report:\n\nAnalysis:\n{{analysis}}\n\nReview:\n{{review}}");
        }
        else
        {
            // 无 Review，直接 Synthesize
            builder.AddSynthesizeStep("synthesize", agentId: options.SynthesizerAgentId)
                .DependsOn("analysis")
                .WithInstructions(options.SynthesizeInstructions
                    ?? "Based on the analysis, produce a comprehensive research report:\n\n{{analysis}}");
        }

        return builder.BuildGraph();
    }
}

/// <summary>
/// 研究模板配置选项
/// </summary>
[ExperimentalApi(Reason = "Workflow templates are in preview")]
public class ResearchTemplateOptions
{
    /// <summary>搜索 Agent ID 列表（多个则并行搜索）</summary>
    public List<Guid>? SearchAgentIds { get; init; }

    /// <summary>分析 Agent ID</summary>
    public required Guid AnalysisAgentId { get; init; }

    /// <summary>审查者 Agent ID（可选，不指定则跳过审查阶段）</summary>
    public Guid? ReviewerAgentId { get; init; }

    /// <summary>综合器 Agent ID（可选）</summary>
    public Guid? SynthesizerAgentId { get; init; }

    /// <summary>搜索自定义指令（可选）</summary>
    public string? SearchInstructions { get; init; }

    /// <summary>分析自定义指令（可选）</summary>
    public string? AnalysisInstructions { get; init; }

    /// <summary>综合自定义指令（可选）</summary>
    public string? SynthesizeInstructions { get; init; }
}
