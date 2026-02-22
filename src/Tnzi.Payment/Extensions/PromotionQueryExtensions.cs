namespace Tnzi.Payment.Extensions;

/// <summary>
/// 促销模块查询扩展方法
/// </summary>
public static class PromotionQueryExtensions
{
    /// <summary>
    /// 根据 PromotionQueryDto 过滤促销记录
    /// </summary>
    public static IQueryable<Promotion> Filter(this IQueryable<Promotion> queryable, PromotionQueryDto query)
    {
        if (!string.IsNullOrEmpty(query.PromotionCode))
            queryable = queryable.Where(p => p.PromotionCode.ToLower().Contains(query.PromotionCode.ToLower()));

        if (query.Type.HasValue)
            queryable = queryable.Where(p => p.Type == query.Type.Value);

        if (query.ProductType.HasValue)
            queryable = queryable.Where(p => p.ProductType == query.ProductType.Value);

        if (query.ActiveOnly)
            queryable = queryable.Where(p => p.IsActive && p.StartTime <= DateTime.UtcNow &&
                (!p.EndTime.HasValue || p.EndTime.Value > DateTime.UtcNow));

        return queryable;
    }
}
