namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 工作流服务接口
/// </summary>
public interface IWorkflowService
{
    /// <summary>
    /// 创建工作流
    /// </summary>
    Task<Result<WorkflowDefinitionDto>> CreateAsync(CreateWorkflowDefinitionDto input);

    /// <summary>
    /// 更新工作流
    /// </summary>
    Task<Result<WorkflowDefinitionDto>> UpdateAsync(Guid id, UpdateWorkflowDefinitionDto input);

    /// <summary>
    /// 删除工作流
    /// </summary>
    Task<Result> DeleteAsync(Guid id);

    /// <summary>
    /// 根据 ID 获取工作流
    /// </summary>
    Task<Result<WorkflowDefinitionDto>> GetByIdAsync(Guid id);

    /// <summary>
    /// 获取工作流列表
    /// </summary>
    Task<Result<IPagedList<WorkflowDefinitionDto>>> GetListAsync(WorkflowDefinitionQueryDto query);

    /// <summary>
    /// 运行工作流
    /// </summary>
    Task<Result<WorkflowExecutionResultDto>> RunAsync(Guid workflowId, string input, Guid? userId = null, CancellationToken ct = default);

    /// <summary>
    /// 流式运行工作流
    /// </summary>
    IAsyncEnumerable<WorkflowExecutionResultDto> RunStreamingAsync(Guid workflowId, string input, Guid? userId = null, CancellationToken ct = default);

    /// <summary>
    /// 恢复暂停的工作流执行
    /// </summary>
    Task<Result<WorkflowExecutionResultDto>> ResumeAsync(string executionId, CancellationToken ct = default);

    /// <summary>
    /// 审批工作流步骤
    /// </summary>
    Task<Result> ApproveStepAsync(string executionId, string stepId, string? feedback = null, CancellationToken ct = default);

    /// <summary>
    /// 拒绝工作流步骤
    /// </summary>
    Task<Result> RejectStepAsync(string executionId, string stepId, string reason, CancellationToken ct = default);

    /// <summary>
    /// 获取工作流执行状态
    /// </summary>
    Task<Result<WorkflowExecutionStatusDto>> GetExecutionStatusAsync(string executionId, CancellationToken ct = default);

    /// <summary>
    /// 克隆工作流（深拷贝定义，生成新 ID）
    /// </summary>
    Task<Result<WorkflowDefinitionDto>> CloneAsync(Guid id, string? newName = null);

    /// <summary>
    /// 批量删除工作流
    /// </summary>
    Task<Result<int>> BatchDeleteAsync(List<Guid> ids);

    /// <summary>
    /// 批量启用/禁用工作流
    /// </summary>
    Task<Result<int>> BatchSetEnabledAsync(List<Guid> ids, bool enabled);

    /// <summary>
    /// 获取工作流统计
    /// </summary>
    Task<Result<WorkflowStatsDto>> GetStatsAsync();

    /// <summary>
    /// 分页查询工作流执行历史
    /// </summary>
    Task<Result<IPagedList<WorkflowExecutionSummaryDto>>> GetExecutionsAsync(WorkflowExecutionQueryDto query);

    /// <summary>
    /// 获取执行详情
    /// </summary>
    Task<Result<WorkflowExecutionDetailDto>> GetExecutionDetailAsync(string executionId);

    /// <summary>
    /// 验证工作流定义（预运行检查）
    /// </summary>
    Task<Result<WorkflowValidationResultDto>> ValidateAsync(Guid workflowId);

    /// <summary>
    /// 获取工作流版本历史
    /// </summary>
    Task<Result<List<WorkflowDefinitionVersionDto>>> GetVersionHistoryAsync(Guid workflowId);

    /// <summary>
    /// 获取指定版本详情
    /// </summary>
    Task<Result<WorkflowDefinitionVersionDto>> GetVersionAsync(Guid workflowId, int versionNumber);

    /// <summary>
    /// 恢复到指定版本
    /// </summary>
    Task<Result> RestoreVersionAsync(Guid workflowId, int versionNumber, string? changeDescription = null);

    /// <summary>
    /// 获取工作流执行统计（耗时分析）
    /// </summary>
    Task<Result<WorkflowExecutionStatsDto>> GetExecutionStatsAsync(Guid workflowId);

    /// <summary>
    /// 使用外部输入恢复中断的工作流（通用 HITL 中断恢复）
    /// </summary>
    /// <param name="executionId">执行 ID</param>
    /// <param name="stepId">中断步骤 ID（用于验证）</param>
    /// <param name="input">外部提供的输入数据</param>
    /// <param name="ct">取消令牌</param>
    [ExperimentalApi(Reason = "Generic workflow interrupt is in preview")]
    Task<Result<WorkflowExecutionResultDto>> ResumeWithInputAsync(string executionId, string stepId, Dictionary<string, object> input, CancellationToken ct = default)
        => Task.FromResult(Result.Failure<WorkflowExecutionResultDto>("ResumeWithInputAsync is not implemented", 501));

    /// <summary>
    /// 获取执行中等待的中断信息
    /// </summary>
    /// <param name="executionId">执行 ID</param>
    /// <param name="ct">取消令牌</param>
    [ExperimentalApi(Reason = "Generic workflow interrupt is in preview")]
    Task<Result<WorkflowInterruptDto>> GetPendingInterruptAsync(string executionId, CancellationToken ct = default)
        => Task.FromResult(Result.Failure<WorkflowInterruptDto>("GetPendingInterruptAsync is not implemented", 501));
}
