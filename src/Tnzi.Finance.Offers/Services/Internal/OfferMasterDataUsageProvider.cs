namespace Tnzi.Finance.Offers.Services.Internal;

/// <summary>
/// 要约模块对 <see cref="IMasterDataUsageProvider"/> 的实现：报价单与采购订单也在引用
/// 客户 / 供应商 / 目录项 / 行科目，所以核心的删除守卫在拆分之后仍然问得到它们。
/// </summary>
/// <remarks>
/// 这是把"会计内核 → 要约模块"的反向依赖翻转过来的那一半：内核只认契约，本模块回答事实。
/// 手法与 <c>Tnzi.Finance.Banking</c> 的 <c>BankStatementHoldProvider</c> 一致。
///
/// ★**这条守卫在拆分之前根本不存在**：<c>CustomerService</c> / <c>VendorService</c> /
/// <c>ItemService</c> 的删除守卫从来只查发票、账单、费用、贷项、收付款，没查过报价单与
/// 采购订单。也就是说删掉一个只出现在报价单上的客户，那张报价单会永久指向一条被软删
/// 因而对全局过滤器不可见的记录 —— 列表里往来方名字变空。拆分让这个洞**必须**现在补：
/// 不补，它就永远补不了（补它需要一条 内核 → 要约模块 的反向引用）。
///
/// 因此本模块的加载**只会增加拒绝**：不加载时删除守卫与拆分前逐字一致（少查两类单据），
/// 加载时多拒绝一批本就该拒绝的删除。绝不会反过来放行任何一个原本被拒绝的操作。
///
/// 只用存在性查询作答：引用者的集合随经营年限只增不减，物化出来是灾难。
/// </remarks>
public class OfferMasterDataUsageProvider : IMasterDataUsageProvider
{
    private readonly IReadOnlyRepository<Estimate, Guid> _estimateRepository;
    private readonly IReadOnlyRepository<PurchaseOrder, Guid> _orderRepository;
    private readonly IReadOnlyRepository<EstimateLine, Guid> _estimateLineRepository;
    private readonly IReadOnlyRepository<PurchaseOrderLine, Guid> _orderLineRepository;

    public OfferMasterDataUsageProvider(
        IReadOnlyRepository<Estimate, Guid> estimateRepository,
        IReadOnlyRepository<PurchaseOrder, Guid> orderRepository,
        IReadOnlyRepository<EstimateLine, Guid> estimateLineRepository,
        IReadOnlyRepository<PurchaseOrderLine, Guid> orderLineRepository)
    {
        _estimateRepository = Check.NotNull(estimateRepository);
        _orderRepository = Check.NotNull(orderRepository);
        _estimateLineRepository = Check.NotNull(estimateLineRepository);
        _orderLineRepository = Check.NotNull(orderLineRepository);
    }

    /// <inheritdoc />
    public async Task<MasterDataUsage?> FindUsageAsync(
        FinanceMasterDataKind kind, Guid id, CancellationToken cancellationToken = default)
    {
        switch (kind)
        {
            case FinanceMasterDataKind.Customer:
                return await _estimateRepository.AnyAsync(e => e.CustomerId == id, cancellationToken)
                    ? new MasterDataUsage("Cannot delete a customer referenced by estimates. Deactivate it instead.")
                    : null;

            case FinanceMasterDataKind.Vendor:
                return await _orderRepository.AnyAsync(o => o.VendorId == id, cancellationToken)
                    ? new MasterDataUsage("Cannot delete a vendor referenced by purchase orders. Deactivate it instead.")
                    : null;

            case FinanceMasterDataKind.Item:
                var used = await _estimateLineRepository.AnyAsync(l => l.ItemId == id, cancellationToken)
                    || await _orderLineRepository.AnyAsync(l => l.ItemId == id, cancellationToken);
                return used
                    ? new MasterDataUsage("Cannot delete an item referenced by estimate or purchase-order lines. Deactivate it instead.")
                    : null;

            case FinanceMasterDataKind.Account:
                // 行科目是转发票 / 转账单时要过账到的科目：只被要约行指着、还没被过账用过的科目
                // 在核心的分录检查里是干净的，删掉后转单那一步才撞上「科目不存在」。
                var posted = await _estimateLineRepository.AnyAsync(l => l.AccountId == id, cancellationToken)
                    || await _orderLineRepository.AnyAsync(l => l.AccountId == id, cancellationToken);
                return posted
                    ? new MasterDataUsage("Cannot delete an account referenced by estimate or purchase-order lines. Edit those lines first.")
                    : null;

            default:
                // 不认识的种类交给别的实现回答，而不是猜一个"没在用"以外的答案。
                return null;
        }
    }
}
