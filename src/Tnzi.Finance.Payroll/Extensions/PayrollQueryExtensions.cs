namespace Tnzi.Finance.Payroll.Extensions;

/// <summary>
/// 薪酬子模块查询扩展方法
/// </summary>
public static class PayrollQueryExtensions
{
    /// <summary>
    /// 根据 EmployeeQueryDto 过滤员工
    /// </summary>
    public static IQueryable<Employee> Filter(this IQueryable<Employee> queryable, EmployeeQueryDto query)
    {
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(e =>
                e.Code.ToLower().Contains(keyword) ||
                e.Name.ToLower().Contains(keyword) ||
                (e.Email != null && e.Email.ToLower().Contains(keyword)));
        }

        if (query.IsActive.HasValue)
            queryable = queryable.Where(e => e.IsActive == query.IsActive.Value);

        return queryable;
    }

    /// <summary>
    /// 根据 SalaryComponentQueryDto 过滤薪资组件
    /// </summary>
    public static IQueryable<SalaryComponent> Filter(this IQueryable<SalaryComponent> queryable, SalaryComponentQueryDto query)
    {
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(c =>
                c.Code.ToLower().Contains(keyword) ||
                c.Name.ToLower().Contains(keyword));
        }

        if (query.Type.HasValue)
            queryable = queryable.Where(c => c.Type == query.Type.Value);

        if (query.IsActive.HasValue)
            queryable = queryable.Where(c => c.IsActive == query.IsActive.Value);

        return queryable;
    }

    /// <summary>
    /// 根据 SalaryStructureQueryDto 过滤薪资结构
    /// </summary>
    public static IQueryable<SalaryStructure> Filter(this IQueryable<SalaryStructure> queryable, SalaryStructureQueryDto query)
    {
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(s => s.Name.ToLower().Contains(keyword));
        }

        if (query.Frequency.HasValue)
            queryable = queryable.Where(s => s.Frequency == query.Frequency.Value);

        if (query.IsActive.HasValue)
            queryable = queryable.Where(s => s.IsActive == query.IsActive.Value);

        return queryable;
    }

    /// <summary>
    /// 根据 BracketTableQueryDto 过滤税级表
    /// </summary>
    public static IQueryable<BracketTable> Filter(this IQueryable<BracketTable> queryable, BracketTableQueryDto query)
    {
        if (!string.IsNullOrEmpty(query.Code))
        {
            var code = query.Code.Trim().ToUpperInvariant();
            queryable = queryable.Where(t => t.Code == code);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(t =>
                t.Code.ToLower().Contains(keyword) ||
                t.Name.ToLower().Contains(keyword));
        }

        if (query.IsActive.HasValue)
            queryable = queryable.Where(t => t.IsActive == query.IsActive.Value);

        return queryable;
    }

    /// <summary>
    /// 根据 PayRunQueryDto 过滤发薪批次
    /// </summary>
    public static IQueryable<PayRun> Filter(this IQueryable<PayRun> queryable, PayRunQueryDto query)
    {
        if (query.Status.HasValue)
            queryable = queryable.Where(r => r.Status == query.Status.Value);

        if (query.Source.HasValue)
            queryable = queryable.Where(r => r.Source == query.Source.Value);

        if (query.DateFrom.HasValue)
        {
            var from = query.DateFrom.Value.ToUtcDate();
            queryable = queryable.Where(r => r.PayDate >= from);
        }

        if (query.DateTo.HasValue)
        {
            var to = query.DateTo.Value.ToUtcDate();
            queryable = queryable.Where(r => r.PayDate <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(r => r.Number != null && r.Number.ToLower().Contains(keyword));
        }

        return queryable;
    }
}
