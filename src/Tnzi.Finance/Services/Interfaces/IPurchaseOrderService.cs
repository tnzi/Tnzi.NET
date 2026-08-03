namespace Tnzi.Finance.Services;

/// <summary>
/// 采购订单服务（**不过账单据**：草稿 → 发出 → 供应商确认/拒绝 → 转账单）
/// </summary>
/// <remarks><see cref="IEstimateService"/> 的镜像，见 <see cref="Entities.PurchaseOrder"/>。</remarks>
public interface IPurchaseOrderService
{
    /// <summary>分页查询</summary>
    Task<Result<IPagedList<PurchaseOrderDto>>> GetPagedAsync(PurchaseOrderQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取单据（含行）</summary>
    Task<Result<PurchaseOrderDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建草稿</summary>
    Task<Result<PurchaseOrderDto>> CreateDraftAsync(CreatePurchaseOrderDto input, CancellationToken cancellationToken = default);

    /// <summary>更新（行全量替换）；已转换的单据不可改</summary>
    Task<Result<PurchaseOrderDto>> UpdateAsync(Guid id, CreatePurchaseOrderDto input, CancellationToken cancellationToken = default);

    /// <summary>删除草稿（**仅**草稿；已发出的订单供应商已经看到，只能关闭）</summary>
    Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>发出（分配连续编号，Draft → Sent）</summary>
    Task<Result<PurchaseOrderDto>> SendAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>供应商确认</summary>
    Task<Result<PurchaseOrderDto>> AcceptAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>供应商拒绝</summary>
    Task<Result<PurchaseOrderDto>> DeclineAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>关闭（作罢、不再跟进）</summary>
    Task<Result<PurchaseOrderDto>> CloseAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>转为账单**草稿**（是否过账由人决定）；重复转换 409</summary>
    Task<Result<ConvertOfferResultDto>> ConvertToBillAsync(Guid id, ConvertOfferDto input, CancellationToken cancellationToken = default);
}
