namespace Tnzi.AI.Templates;

/// <summary>
/// 并行团队模板。
/// 将任务分发给多个 Worker 并行执行 → Synthesize 汇总
/// </summary>
/// <remarks>
/// 典型场景：多视角分析（安全审查 + 性能审查 + 代码风格审查）、
/// 多语言翻译、多源数据收集等需要并行处理后汇总的任务。
/// </remarks>
[ExperimentalApi(Reason = "Workflow templates are in preview")]
public static class ParallelTeamTemplate
{
    /// <summary>
    /// 构建并行团队工作流图
    /// </summary>
    public static WorkflowGraph Build(ParallelTeamTemplateOptions options)
    {
        Check.NotNull(options);
        Check.NotNullOrEmpty(options.Workers);

        var builder = WorkflowBuilder.Create();

        // 1. Parallel 节点 — 分发给多个 Worker 并行执行
        var workers = options.Workers.Select((w, i) => new Dictionary<string, object>
        {
            ["stepId"] = w.Name ?? $"worker-{i + 1}",
            ["agentId"] = w.AgentId
        });
        var workersJson = JsonSerializer.Serialize(workers, TnziJsonDefaults.Options);

        builder.AddParallelStep("parallel-workers")
            .WithConfiguration("workers", workersJson);

        // 2. Synthesize 节点 — 汇总所有 Worker 的输出
        builder.AddSynthesizeStep("synthesize", agentId: options.SynthesizerAgentId)
            .DependsOn("parallel-workers")
            .WithInstructions(options.SynthesizeInstructions
                ?? "Synthesize the following worker outputs into a coherent, comprehensive response:\n\n{{parallel-workers}}");

        return builder.BuildGraph();
    }
}

/// <summary>
/// 并行团队模板配置选项
/// </summary>
[ExperimentalApi(Reason = "Workflow templates are in preview")]
public class ParallelTeamTemplateOptions
{
    /// <summary>Worker 列表（至少 2 个）</summary>
    public required List<ParallelWorkerConfig> Workers { get; init; }

    /// <summary>综合器 Agent ID（可选）</summary>
    public Guid? SynthesizerAgentId { get; init; }

    /// <summary>综合器自定义指令（可选）</summary>
    public string? SynthesizeInstructions { get; init; }
}

/// <summary>
/// 并行 Worker 配置
/// </summary>
[ExperimentalApi(Reason = "Workflow templates are in preview")]
public class ParallelWorkerConfig
{
    /// <summary>Worker 名称（用于标识输出）</summary>
    public string? Name { get; init; }

    /// <summary>Worker Agent ID</summary>
    public required Guid AgentId { get; init; }
}
