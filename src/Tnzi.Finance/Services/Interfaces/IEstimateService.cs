namespace Tnzi.Finance.Services;

/// <summary>
/// 报价单服务（**不过账单据**：草稿 → 发出 → 接受/拒绝 → 转发票）
/// </summary>
/// <remarks>
/// 与 A/R 单据的差异见 <see cref="Entities.Estimate"/>。生命周期动作刻意用业务
/// 词（Send / Accept / Decline / Close）而不是 Post / Void：报价单从不进总账，
/// 借用会计动词只会让人以为它进了。
/// </remarks>
public interface IEstimateService
{
    /// <summary>分页查询</summary>
    Task<Result<IPagedList<EstimateDto>>> GetPagedAsync(EstimateQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取单据（含行）</summary>
    Task<Result<EstimateDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建草稿</summary>
    Task<Result<EstimateDto>> CreateDraftAsync(CreateEstimateDto input, CancellationToken cancellationToken = default);

    /// <summary>更新（行全量替换）；已转换的单据不可改</summary>
    Task<Result<EstimateDto>> UpdateAsync(Guid id, CreateEstimateDto input, CancellationToken cancellationToken = default);

    /// <summary>删除草稿（**仅**草稿；已发出的单据对方已经看到，只能关闭）</summary>
    Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>发出（分配连续编号，Draft → Sent）</summary>
    Task<Result<EstimateDto>> SendAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>客户接受</summary>
    Task<Result<EstimateDto>> AcceptAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>客户拒绝</summary>
    Task<Result<EstimateDto>> DeclineAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>关闭（过期、作罢、不再跟进）</summary>
    Task<Result<EstimateDto>> CloseAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>转为发票**草稿**（是否过账由人决定）；重复转换 409</summary>
    Task<Result<ConvertOfferResultDto>> ConvertToInvoiceAsync(Guid id, ConvertOfferDto input, CancellationToken cancellationToken = default);
}
