namespace Tnzi.Payment.Extensions;

/// <summary>
/// 支付模块查询扩展方法
/// </summary>
public static class PaymentQueryExtensions
{
    /// <summary>
    /// 根据 PaymentQueryDto 过滤支付记录
    /// </summary>
    public static IQueryable<PaymentEntity> Filter(this IQueryable<PaymentEntity> queryable, PaymentQueryDto query)
    {
        // 系统生成的流水号使用精确匹配（支持索引），用户输入字段使用模糊匹配
        if (!string.IsNullOrEmpty(query.TradeNo))
            queryable = queryable.Where(p => p.TradeNo == query.TradeNo);

        if (!string.IsNullOrEmpty(query.ExternalTradeNo))
            queryable = queryable.Where(p => p.ExternalTradeNo == query.ExternalTradeNo);

        if (!string.IsNullOrEmpty(query.BusinessOrderNo))
            queryable = queryable.Where(p => p.BusinessOrderNo == query.BusinessOrderNo);

        if (query.BusinessType.HasValue)
            queryable = queryable.Where(p => p.BusinessType == query.BusinessType.Value);

        if (query.Status.HasValue)
            queryable = queryable.Where(p => p.Status == query.Status.Value);

        if (query.ChannelCode != null)
            queryable = queryable.Where(p => p.ChannelCode == query.ChannelCode);

        if (query.UserId.HasValue)
            queryable = queryable.Where(p => p.UserId == query.UserId.Value);

        if (query.StartTime.HasValue)
            queryable = queryable.Where(p => p.CreationTime >= query.StartTime.Value);

        if (query.EndTime.HasValue)
            queryable = queryable.Where(p => p.CreationTime <= query.EndTime.Value);

        return queryable;
    }
}
