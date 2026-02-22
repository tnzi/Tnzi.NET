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
        if (!string.IsNullOrEmpty(query.InvoiceNo))
            queryable = queryable.Where(i => i.InvoiceNo.ToLower().Contains(query.InvoiceNo.ToLower()));

        if (query.Type.HasValue)
            queryable = queryable.Where(i => i.Type == query.Type.Value);

        if (query.Status.HasValue)
            queryable = queryable.Where(i => i.Status == query.Status.Value);

        if (!string.IsNullOrEmpty(query.CustomerEmail))
            queryable = queryable.Where(i => i.CustomerEmail.ToLower().Contains(query.CustomerEmail.ToLower()));

        if (query.StartTime.HasValue)
            queryable = queryable.Where(i => i.InvoiceDate >= query.StartTime.Value);

        if (query.EndTime.HasValue)
            queryable = queryable.Where(i => i.InvoiceDate <= query.EndTime.Value);

        return queryable;
    }
}
