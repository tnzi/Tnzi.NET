
namespace Tnzi.AI.Templates;

/// <summary>
/// Supervisor 模式模板。
/// Router → 按类别并行 Workers → Review → (Rework 循环) → Synthesize
/// </summary>
/// <remarks>
/// 典型场景：客服系统中 Router 分类问题，分配给对应专家 Worker 处理，Reviewer 审查质量，
/// 不合格则 Rework，最终由 Synthesizer 汇总输出。
/// </remarks>
[ExperimentalApi(Reason = "Workflow templates are in preview")]
public static class SupervisorTemplate
{
    /// <summary>
    /// 构建 Supervisor 模式工作流图
    /// </summary>
    public static WorkflowGraph Build(SupervisorTemplateOptions options)
    {
        Check.NotNull(options);
        Check.NotNullOrEmpty(options.Routes);
        Check.NotNullOrEmpty(options.CategoryWorkers);

        var builder = WorkflowBuilder.Create();

        // 1. Router 节点 — 分类输入
        var routesJson = JsonSerializer.Serialize(options.Routes, TnziJsonDefaults.Options);
        builder.AddRouterStep("router", agentId: options.RouterAgentId)
            .WithConfiguration("routes", routesJson);

        // 2. 为每个类别添加 Parallel Worker 节点
        var workerStepIds = new List<string>();
        foreach (var (category, workerAgentIds) in options.CategoryWorkers)
        {
            var stepId = $"workers-{category}";
            workerStepIds.Add(stepId);

            var workers = workerAgentIds.Select((id, i) => new { stepId = $"{category}-w{i + 1}", agentId = id });
            var workersJson = JsonSerializer.Serialize(
                workers.Select(w => new Dictionary<string, object> { ["stepId"] = w.stepId, ["agentId"] = w.agentId }),
                TnziJsonDefaults.Options);

            builder.AddParallelStep(stepId)
                .DependsOn("router")
                .WithConfiguration("workers", workersJson);
        }

        // 3. 条件边 — Router 路由到对应 Worker
        var routeEdges = new Dictionary<string, string>();
        foreach (var category in options.CategoryWorkers.Keys)
        {
            routeEdges[category] = $"workers-{category}";
        }
        builder.AddConditionalEdge("router", routeEdges, defaultTarget: workerStepIds[0]);

        // 4. Review-Rework 循环
        builder.AddLoop("review-loop", options.MaxReworkRounds, loopBuilder =>
        {
            // Review 节点 — 审查 Worker 输出
            var reviewStep = loopBuilder.AddReviewStep("review", agentId: options.ReviewerAgentId);
            foreach (var stepId in workerStepIds)
            {
                reviewStep.DependsOn(stepId);
            }

            // Rework 节点 — 如果 Review verdict = rework，重新处理
            loopBuilder.AddStep("rework", agentId: workerStepIds.Count == 1
                ? options.CategoryWorkers.Values.First().FirstOrDefault()
                : null)
                .DependsOn("review")
                .WithInstructions("Please revise your work based on the reviewer's feedback:\n{{review}}");

            // 条件边: review → accept 跳出循环，rework 继续
            loopBuilder.AddConditionalEdge("review", new Dictionary<string, string>
            {
                ["accept"] = "synthesize",
                ["rework"] = "rework"
            }, defaultTarget: "synthesize");
        });

        // 5. Synthesize 节点 — 汇总最终结果
        var synthStep = builder.AddSynthesizeStep("synthesize", agentId: options.SynthesizerAgentId);
        synthStep.DependsOn("review");

        return builder.BuildGraph();
    }
}

/// <summary>
/// Supervisor 模板配置选项
/// </summary>
[ExperimentalApi(Reason = "Workflow templates are in preview")]
public class SupervisorTemplateOptions
{
    /// <summary>路由器 Agent ID</summary>
    public required Guid RouterAgentId { get; init; }

    /// <summary>路由映射（Key: 路由键, Value: 描述）</summary>
    public required Dictionary<string, string> Routes { get; init; }

    /// <summary>每个类别的 Worker Agent ID 列表</summary>
    public required Dictionary<string, List<Guid>> CategoryWorkers { get; init; }

    /// <summary>审查者 Agent ID</summary>
    public required Guid ReviewerAgentId { get; init; }

    /// <summary>综合器 Agent ID（可选，不指定则由系统默认生成）</summary>
    public Guid? SynthesizerAgentId { get; init; }

    /// <summary>最大 Rework 轮数（默认 3）</summary>
    public int MaxReworkRounds { get; init; } = 3;
}
