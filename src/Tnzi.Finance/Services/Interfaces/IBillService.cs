namespace Tnzi.Finance.Services;

/// <summary>
/// 采购账单服务（单据范式：草稿工作流 + 过账 + 作废）
/// </summary>
public interface IBillService
{
    /// <summary>分页查询</summary>
    Task<Result<IPagedList<BillDto>>> GetPagedAsync(BillQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取单据（含行）</summary>
    Task<Result<BillDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建草稿</summary>
    Task<Result<BillDto>> CreateDraftAsync(CreateBillDto input, CancellationToken cancellationToken = default);

    /// <summary>更新草稿（行全量替换）</summary>
    Task<Result<BillDto>> UpdateDraftAsync(Guid id, CreateBillDto input, CancellationToken cancellationToken = default);

    /// <summary>删除草稿</summary>
    Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>过账（分配编号并投影到总账）</summary>
    Task<Result<BillDto>> PostAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>作废（冲销过账凭证）</summary>
    Task<Result<BillDto>> VoidAsync(Guid id, CancellationToken cancellationToken = default);
}
