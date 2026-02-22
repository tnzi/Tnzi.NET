namespace Tnzi.Payment.Extensions;

/// <summary>
/// 退款模块查询扩展方法
/// </summary>
public static class RefundQueryExtensions
{
    /// <summary>
    /// 根据 RefundQueryDto 过滤退款记录
    /// </summary>
    public static IQueryable<Refund> Filter(this IQueryable<Refund> queryable, RefundQueryDto query)
    {
        if (!string.IsNullOrEmpty(query.TradeNo))
            queryable = queryable.Where(r => r.Payment!.TradeNo.ToLower().Contains(query.TradeNo.ToLower()));

        if (!string.IsNullOrEmpty(query.RefundNo))
            queryable = queryable.Where(r => r.RefundNo.ToLower().Contains(query.RefundNo.ToLower()));

        if (query.Status.HasValue)
            queryable = queryable.Where(r => r.Status == query.Status.Value);

        if (query.StartTime.HasValue)
            queryable = queryable.Where(r => r.CreationTime >= query.StartTime.Value);

        if (query.EndTime.HasValue)
            queryable = queryable.Where(r => r.CreationTime <= query.EndTime.Value);

        return queryable;
    }
}
