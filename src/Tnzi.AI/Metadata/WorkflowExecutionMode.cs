namespace Tnzi.AI.Metadata;

/// <summary>
/// 工作流执行模式
/// </summary>
public enum WorkflowExecutionMode
{
    /// <summary>
    /// 顺序执行 — Agent 按顺序依次执行
    /// </summary>
    Sequential,

    /// <summary>
    /// 并行执行 — Agent 同时执行
    /// </summary>
    Parallel,

    /// <summary>
    /// DAG 执行 — 按依赖关系拓扑排序，同层无依赖步骤自动并行
    /// </summary>
    Dag
}
