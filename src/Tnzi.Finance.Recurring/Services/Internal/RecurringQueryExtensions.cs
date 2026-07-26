namespace Tnzi.Finance.Recurring.Services.Internal;

/// <summary>
/// 查询过滤（与 Finance 核心的 <c>FinanceQueryExtensions</c> 同一手法：
/// 过滤条件收口一处，页面与导出不会各自漂移）
/// </summary>
internal static class RecurringQueryExtensions
{
    public static IQueryable<RecurringDocument> Filter(this IQueryable<RecurringDocument> query, RecurringDocumentQueryDto q)
    {
        if (!string.IsNullOrWhiteSpace(q.Keyword))
        {
            var keyword = q.Keyword.Trim().ToLower();
            query = query.Where(e => e.Name.ToLower().Contains(keyword)
                || (e.Memo != null && e.Memo.ToLower().Contains(keyword)));
        }

        if (q.Kind.HasValue)
            query = query.Where(e => e.Kind == q.Kind.Value);

        if (q.Status.HasValue)
            query = query.Where(e => e.Status == q.Status.Value);

        if (q.PartyId.HasValue)
            query = query.Where(e => e.PartyId == q.PartyId.Value);

        if (q.DueBefore.HasValue)
        {
            var due = q.DueBefore.Value.ToUtcDate();
            query = query.Where(e => e.NextRunDate <= due);
        }

        return query;
    }

    public static IQueryable<RecurringRun> Filter(this IQueryable<RecurringRun> query, RecurringRunQueryDto q)
    {
        if (q.RecurringDocumentId.HasValue)
            query = query.Where(e => e.RecurringDocumentId == q.RecurringDocumentId.Value);

        if (q.Status.HasValue)
            query = query.Where(e => e.Status == q.Status.Value);

        if (q.From.HasValue)
        {
            var from = q.From.Value.ToUtcDate();
            query = query.Where(e => e.PeriodDate >= from);
        }

        if (q.To.HasValue)
        {
            var to = q.To.Value.ToUtcDate();
            query = query.Where(e => e.PeriodDate <= to);
        }

        return query;
    }
}
