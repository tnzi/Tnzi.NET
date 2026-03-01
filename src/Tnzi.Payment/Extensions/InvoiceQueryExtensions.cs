namespace Tnzi.Payment.Extensions;

/// <summary>
/// 发票模块查询扩展方法
/// </summary>
public static class InvoiceQueryExtensions
{
    /// <summary>
    /// 根据 InvoiceQueryDto 过滤发票记录
    /// </summary>
    public static IQueryable<Invoice> Filter(this IQueryable<Invoice> queryable, InvoiceQueryDto query)
    {
        // 系统生成的流水号使用精确匹配（支持索引）
        if (!string.IsNullOrEmpty(query.InvoiceNo))
            queryable = queryable.Where(i => i.InvoiceNo == query.InvoiceNo);

        if (query.Type.HasValue)
            queryable = queryable.Where(i => i.Type == query.Type.Value);

        if (query.Status.HasValue)
            queryable = queryable.Where(i => i.Status == query.Status.Value);

        if (!string.IsNullOrEmpty(query.CustomerEmail))
            queryable = queryable.Where(i => i.CustomerEmail == query.CustomerEmail);

        if (query.StartTime.HasValue)
            queryable = queryable.Where(i => i.InvoiceDate >= query.StartTime.Value);

        if (query.EndTime.HasValue)
            queryable = queryable.Where(i => i.InvoiceDate <= query.EndTime.Value);

        return queryable;
    }
}
