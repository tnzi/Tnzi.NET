namespace Tnzi.Finance.Services;

/// <summary>
/// 资金划转单服务（单据范式：草稿工作流 + 过账 + 作废冲销）
/// </summary>
public interface ITransferService
{
    /// <summary>分页查询划转单</summary>
    Task<Result<IPagedList<TransferDto>>> GetPagedAsync(TransferQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取划转单</summary>
    Task<Result<TransferDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建草稿（双方须为可过账资金叶子科目，即 CashFlowActivity = CashEquivalent）</summary>
    Task<Result<TransferDto>> CreateDraftAsync(CreateTransferDto input, CancellationToken cancellationToken = default);

    /// <summary>更新草稿</summary>
    Task<Result<TransferDto>> UpdateDraftAsync(Guid id, CreateTransferDto input, CancellationToken cancellationToken = default);

    /// <summary>删除草稿</summary>
    Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>过账（借 转入科目 / 贷 转出科目；编号过账时分配）</summary>
    Task<Result<TransferDto>> PostAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>作废（冲销过账凭证）</summary>
    Task<Result<TransferDto>> VoidAsync(Guid id, CancellationToken cancellationToken = default);
}
