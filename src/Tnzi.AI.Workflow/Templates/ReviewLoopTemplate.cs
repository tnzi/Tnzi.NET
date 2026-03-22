
namespace Tnzi.AI.Templates;

/// <summary>
/// Review-Rework 循环模板。
/// Worker → Review → (条件) → Rework 或 Accept
/// </summary>
/// <remarks>
/// 典型场景：代码审查、文档校对、内容创作中需要反复修改直到通过审查。
/// </remarks>
[ExperimentalApi(Reason = "Workflow templates are in preview")]
public static class ReviewLoopTemplate
{
    /// <summary>
    /// 构建 Review-Rework 循环工作流图
    /// </summary>
    public static WorkflowGraph Build(ReviewLoopTemplateOptions options)
    {
        Check.NotNull(options);

        var builder = WorkflowBuilder.Create();

        // 1. Worker 节点 — 初始处理
        builder.AddStep("worker", agentId: options.WorkerAgentId)
            .WithInstructions(options.WorkerInstructions ?? "Process the following input:\n{{input}}");

        // 2. Review-Rework 循环
        builder.AddLoop("review-loop", options.MaxReworkRounds, loopBuilder =>
        {
            // Review 节点
            loopBuilder.AddReviewStep("review", agentId: options.ReviewerAgentId)
                .DependsOn("worker")
                .WithInstructions(options.ReviewInstructions
                    ?? "Review the worker output and decide whether it should be accepted or reworked.\n\nWorker output:\n{{worker}}");

            // Rework 节点 — 使用原 Worker Agent 重新处理
            loopBuilder.AddStep("rework", agentId: options.WorkerAgentId)
                .DependsOn("review")
                .WithInstructions(options.ReworkInstructions
                    ?? "Please revise your work based on the reviewer's feedback:\n\nOriginal work:\n{{worker}}\n\nFeedback:\n{{review}}");

            // 条件边: review → accept/rework
            loopBuilder.AddConditionalEdge("review", new Dictionary<string, string>
            {
                ["accept"] = "output",
                ["rework"] = "rework"
            }, defaultTarget: "output");
        });

        // 3. Output 节点 — 最终输出（透传审查通过的内容）
        builder.AddTransformStep("output")
            .DependsOn("review")
            .WithConfiguration("template", "{{worker}}");

        return builder.BuildGraph();
    }
}

/// <summary>
/// Review-Rework 循环模板配置选项
/// </summary>
[ExperimentalApi(Reason = "Workflow templates are in preview")]
public class ReviewLoopTemplateOptions
{
    /// <summary>Worker Agent ID</summary>
    public required Guid WorkerAgentId { get; init; }

    /// <summary>Reviewer Agent ID</summary>
    public required Guid ReviewerAgentId { get; init; }

    /// <summary>Worker 自定义指令（可选）</summary>
    public string? WorkerInstructions { get; init; }

    /// <summary>Review 自定义指令（可选）</summary>
    public string? ReviewInstructions { get; init; }

    /// <summary>Rework 自定义指令（可选）</summary>
    public string? ReworkInstructions { get; init; }

    /// <summary>最大 Rework 轮数（默认 3）</summary>
    public int MaxReworkRounds { get; init; } = 3;
}
