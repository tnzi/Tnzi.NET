namespace Tnzi.AI.Workflow;

/// <summary>
/// 工作流检查点存储抽象 - 持久化工作流执行状态，支持断点续执行
/// </summary>
/// <remarks>
/// 默认实现为 DatabaseWorkflowCheckpointStore（基于 IRepository）。
/// 用户可通过 DI 注册自定义实现（如 Redis、文件系统等）。
/// </remarks>
[ExperimentalApi(Reason = "Workflow checkpointing is in preview")]
public interface IWorkflowCheckpointStore
{
    /// <summary>
    /// 保存检查点（插入或更新）
    /// </summary>
    /// <param name="checkpoint">检查点数据</param>
    /// <param name="ct">取消令牌</param>
    Task SaveCheckpointAsync(WorkflowCheckpoint checkpoint, CancellationToken ct = default);

    /// <summary>
    /// 获取检查点
    /// </summary>
    /// <param name="executionId">执行实例 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>检查点数据，不存在则返回 null</returns>
    Task<WorkflowCheckpoint?> GetCheckpointAsync(string executionId, CancellationToken ct = default);

    /// <summary>
    /// 删除检查点
    /// </summary>
    /// <param name="executionId">执行实例 ID</param>
    /// <param name="ct">取消令牌</param>
    Task DeleteCheckpointAsync(string executionId, CancellationToken ct = default);
}
