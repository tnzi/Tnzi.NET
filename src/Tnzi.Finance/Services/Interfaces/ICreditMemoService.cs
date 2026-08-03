namespace Tnzi.Finance.Services;

/// <summary>
/// 销售贷项单服务（单据范式：草稿工作流 + 过账 + 作废）
/// </summary>
public interface ICreditMemoService
{
    /// <summary>分页查询</summary>
    Task<Result<IPagedList<CreditMemoDto>>> GetPagedAsync(CreditMemoQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取单据（含行）</summary>
    Task<Result<CreditMemoDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建草稿</summary>
    Task<Result<CreditMemoDto>> CreateDraftAsync(CreateCreditMemoDto input, CancellationToken cancellationToken = default);

    /// <summary>更新草稿（行全量替换）</summary>
    Task<Result<CreditMemoDto>> UpdateDraftAsync(Guid id, CreateCreditMemoDto input, CancellationToken cancellationToken = default);

    /// <summary>删除草稿</summary>
    Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>过账（分配编号并投影到总账）</summary>
    Task<Result<CreditMemoDto>> PostAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>作废（冲销过账凭证）</summary>
    Task<Result<CreditMemoDto>> VoidAsync(Guid id, CancellationToken cancellationToken = default);
}
