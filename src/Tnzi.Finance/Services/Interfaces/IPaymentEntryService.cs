namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 收付款单服务（单据范式：草稿工作流 + 过账 + 作废；核销见 P2c 结算服务）
/// </summary>
public interface IPaymentEntryService
{
    /// <summary>分页查询</summary>
    Task<Result<IPagedList<PaymentEntryDto>>> GetPagedAsync(PaymentEntryQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取单据</summary>
    Task<Result<PaymentEntryDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建草稿</summary>
    Task<Result<PaymentEntryDto>> CreateDraftAsync(CreatePaymentEntryDto input, CancellationToken cancellationToken = default);

    /// <summary>更新草稿</summary>
    Task<Result<PaymentEntryDto>> UpdateDraftAsync(Guid id, CreatePaymentEntryDto input, CancellationToken cancellationToken = default);

    /// <summary>删除草稿</summary>
    Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>过账（分配编号并投影到总账）</summary>
    Task<Result<PaymentEntryDto>> PostAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>作废（冲销过账凭证；已核销时拒绝）</summary>
    Task<Result<PaymentEntryDto>> VoidAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 外部收款摄取（网关收款 → 收款单）：按 SourceType + SourceId 幂等，
    /// 已存在时返回既有单据；AutoPost 时立即过账
    /// </summary>
    Task<Result<PaymentEntryDto>> CreateFromExternalAsync(ExternalPaymentIngestDto input, CancellationToken cancellationToken = default);
}
