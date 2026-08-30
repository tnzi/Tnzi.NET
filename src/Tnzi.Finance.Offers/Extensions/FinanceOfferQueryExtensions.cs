namespace Tnzi.Finance.Offers.Extensions;

/// <summary>
/// 报价单 / 采购订单查询扩展方法
/// </summary>
/// <remarks>
/// ★**这是一处刻意的源码可见破坏**：这两个 <c>Filter</c> 重载原先与核心的其余重载
/// 同住 <c>Tnzi.Finance.Extensions.FinanceQueryExtensions</c>，现在改到
/// <c>Tnzi.Finance.Offers.Extensions</c>。命名空间约定门禁（R1，
/// <c>Tnzi.Tests/Architecture/NamespaceConventionTests</c>）不允许 <c>Tnzi.Finance.Offers</c>
/// 程序集声明 <c>Tnzi.Finance.Extensions</c> —— 跨程序集占名会让消费方以为类型在别的包里，
/// 也会在两个包同时加载时产生歧义。
///
/// 扩展方法的可用性绑在 <c>using</c> 上（编码规范 R5），所以直接对
/// <c>IQueryable&lt;Estimate&gt;</c> / <c>IQueryable&lt;PurchaseOrder&gt;</c> 调 <c>.Filter(query)</c>
/// 的代码需要**多写一行 using**。方法名、签名与行为一字不变，改动只有 using 一行；
/// 框架内的调用方只有本模块自己的两个服务。
/// </remarks>
public static class FinanceOfferQueryExtensions
{
    /// <summary>
    /// 根据 EstimateQueryDto 过滤报价单
    /// </summary>
    public static IQueryable<Estimate> Filter(this IQueryable<Estimate> queryable, EstimateQueryDto query)
    {
        if (query.Status.HasValue)
            queryable = queryable.Where(d => d.Status == query.Status.Value);

        // "仍在流转中" = 还可能变成一张发票的那些；已转换/已拒绝/已关闭都出局。
        if (query.OpenOnly == true)
            queryable = queryable.Where(d =>
                d.Status == FinanceOfferStatus.Draft ||
                d.Status == FinanceOfferStatus.Sent ||
                d.Status == FinanceOfferStatus.Accepted);

        if (query.CustomerId.HasValue)
            queryable = queryable.Where(d => d.CustomerId == query.CustomerId.Value);

        if (query.DateFrom.HasValue)
        {
            var from = query.DateFrom.Value.ToUtcDate();
            queryable = queryable.Where(d => d.DocDate >= from);
        }

        if (query.DateTo.HasValue)
        {
            var toExclusive = query.DateTo.Value.ToUtcDate().AddDays(1);
            queryable = queryable.Where(d => d.DocDate < toExclusive);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(d =>
                (d.Number != null && d.Number.ToLower().Contains(keyword)) ||
                (d.Memo != null && d.Memo.ToLower().Contains(keyword)));
        }

        return queryable;
    }

    /// <summary>
    /// 根据 PurchaseOrderQueryDto 过滤采购订单
    /// </summary>
    public static IQueryable<PurchaseOrder> Filter(this IQueryable<PurchaseOrder> queryable, PurchaseOrderQueryDto query)
    {
        if (query.Status.HasValue)
            queryable = queryable.Where(d => d.Status == query.Status.Value);

        if (query.OpenOnly == true)
            queryable = queryable.Where(d =>
                d.Status == FinanceOfferStatus.Draft ||
                d.Status == FinanceOfferStatus.Sent ||
                d.Status == FinanceOfferStatus.Accepted);

        if (query.VendorId.HasValue)
            queryable = queryable.Where(d => d.VendorId == query.VendorId.Value);

        if (query.DateFrom.HasValue)
        {
            var from = query.DateFrom.Value.ToUtcDate();
            queryable = queryable.Where(d => d.DocDate >= from);
        }

        if (query.DateTo.HasValue)
        {
            var toExclusive = query.DateTo.Value.ToUtcDate().AddDays(1);
            queryable = queryable.Where(d => d.DocDate < toExclusive);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(d =>
                (d.Number != null && d.Number.ToLower().Contains(keyword)) ||
                (d.Memo != null && d.Memo.ToLower().Contains(keyword)));
        }

        return queryable;
    }
}
