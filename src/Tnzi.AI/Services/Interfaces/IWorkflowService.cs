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
    Task<Result<IPagedList<WorkflowDefinitionDto>>> GetListAsync(PagedQueryDto query);

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
}
