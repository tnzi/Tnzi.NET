namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 评估运行管理服务接口 — 提供评估历史的只读查询和删除
/// </summary>
public interface IEvaluationService
{
    /// <summary>
    /// 根据 ID 获取评估运行详情
    /// </summary>
    Task<Result<EvaluationRunDetailDto>> GetByIdAsync(Guid id);

    /// <summary>
    /// 分页查询评估运行列表（支持 AgentId、Status 过滤）
    /// </summary>
    Task<Result<IPagedList<EvaluationRunDto>>> GetListAsync(EvaluationRunQueryDto query);

    /// <summary>
    /// 删除评估运行记录
    /// </summary>
    Task<Result> DeleteAsync(Guid id);

    /// <summary>
    /// 创建并执行评估运行
    /// </summary>
    Task<Result<EvaluationRunDetailDto>> CreateAndRunAsync(CreateEvaluationRunDto dto, CancellationToken ct = default);

    /// <summary>
    /// 批量评估多个 Agent/版本（并行执行）
    /// </summary>
    Task<Result<BatchEvaluationResultDto>> RunBatchAsync(BatchEvaluationDto dto, CancellationToken ct = default);
}
