namespace Tnzi.Payment.Extensions;

/// <summary>
/// 订阅模块查询扩展方法
/// </summary>
public static class SubscriptionQueryExtensions
{
    /// <summary>
    /// 根据 SubscriptionQueryDto 过滤订阅记录
    /// </summary>
    public static IQueryable<Subscription> Filter(this IQueryable<Subscription> queryable, SubscriptionQueryDto query)
    {
        if (query.UserId.HasValue)
            queryable = queryable.Where(s => s.UserId == query.UserId.Value);

        if (query.Status.HasValue)
            queryable = queryable.Where(s => s.Status == query.Status.Value);

        if (query.PlanId.HasValue)
            queryable = queryable.Where(s => s.PlanId == query.PlanId.Value);

        if (query.AutoRenew.HasValue)
            queryable = queryable.Where(s => s.AutoRenew == query.AutoRenew.Value);

        return queryable;
    }
}
