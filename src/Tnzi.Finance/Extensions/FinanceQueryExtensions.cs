namespace Tnzi.Finance.Extensions;

/// <summary>
/// 财务模块查询扩展方法
/// </summary>
public static class FinanceQueryExtensions
{
    /// <summary>
    /// 根据 AccountQueryDto 过滤科目
    /// </summary>
    public static IQueryable<Account> Filter(this IQueryable<Account> queryable, AccountQueryDto query)
    {
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(a => a.Code.ToLower().Contains(keyword) || a.Name.ToLower().Contains(keyword));
        }

        if (query.RootType.HasValue)
            queryable = queryable.Where(a => a.RootType == query.RootType.Value);

        if (query.IsActive.HasValue)
            queryable = queryable.Where(a => a.IsActive == query.IsActive.Value);

        return queryable;
    }

    /// <summary>
    /// 根据 JournalEntryQueryDto 过滤凭证
    /// </summary>
    public static IQueryable<JournalEntry> Filter(this IQueryable<JournalEntry> queryable, JournalEntryQueryDto query)
    {
        if (query.Status.HasValue)
            queryable = queryable.Where(e => e.Status == query.Status.Value);

        if (query.DateFrom.HasValue)
        {
            var from = query.DateFrom.Value.ToUtcDate();
            queryable = queryable.Where(e => e.PostingDate >= from);
        }

        if (query.DateTo.HasValue)
        {
            var toExclusive = query.DateTo.Value.ToUtcDate().AddDays(1);
            queryable = queryable.Where(e => e.PostingDate < toExclusive);
        }

        if (!string.IsNullOrEmpty(query.SourceType))
            queryable = queryable.Where(e => e.SourceType == query.SourceType);

        if (!string.IsNullOrEmpty(query.SourceId))
            queryable = queryable.Where(e => e.SourceId == query.SourceId);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(e =>
                (e.Number != null && e.Number.ToLower().Contains(keyword)) ||
                (e.Memo != null && e.Memo.ToLower().Contains(keyword)));
        }

        return queryable;
    }

    /// <summary>
    /// 根据 ExchangeRateQueryDto 过滤汇率
    /// </summary>
    public static IQueryable<ExchangeRate> Filter(this IQueryable<ExchangeRate> queryable, ExchangeRateQueryDto query)
    {
        if (!string.IsNullOrEmpty(query.FromCurrency))
            queryable = queryable.Where(r => r.FromCurrency == query.FromCurrency);

        if (!string.IsNullOrEmpty(query.ToCurrency))
            queryable = queryable.Where(r => r.ToCurrency == query.ToCurrency);

        if (query.DateFrom.HasValue)
        {
            var from = query.DateFrom.Value.ToUtcDate();
            queryable = queryable.Where(r => r.RateDate >= from);
        }

        if (query.DateTo.HasValue)
        {
            var toExclusive = query.DateTo.Value.ToUtcDate().AddDays(1);
            queryable = queryable.Where(r => r.RateDate < toExclusive);
        }

        return queryable;
    }

    /// <summary>
    /// 根据 CustomerQueryDto 过滤客户
    /// </summary>
    public static IQueryable<Customer> Filter(this IQueryable<Customer> queryable, CustomerQueryDto query)
    {
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(c =>
                c.Name.ToLower().Contains(keyword) ||
                (c.Code != null && c.Code.ToLower().Contains(keyword)) ||
                (c.Email != null && c.Email.ToLower().Contains(keyword)));
        }

        if (query.IsActive.HasValue)
            queryable = queryable.Where(c => c.IsActive == query.IsActive.Value);

        return queryable;
    }

    /// <summary>
    /// 根据 VendorQueryDto 过滤供应商
    /// </summary>
    public static IQueryable<Vendor> Filter(this IQueryable<Vendor> queryable, VendorQueryDto query)
    {
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(v =>
                v.Name.ToLower().Contains(keyword) ||
                (v.Code != null && v.Code.ToLower().Contains(keyword)) ||
                (v.Email != null && v.Email.ToLower().Contains(keyword)));
        }

        if (query.IsActive.HasValue)
            queryable = queryable.Where(v => v.IsActive == query.IsActive.Value);

        return queryable;
    }

    /// <summary>
    /// 根据 ItemQueryDto 过滤目录项
    /// </summary>
    public static IQueryable<Item> Filter(this IQueryable<Item> queryable, ItemQueryDto query)
    {
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(i =>
                i.Name.ToLower().Contains(keyword) ||
                (i.Code != null && i.Code.ToLower().Contains(keyword)));
        }

        if (query.Type.HasValue)
            queryable = queryable.Where(i => i.Type == query.Type.Value);

        if (query.IsActive.HasValue)
            queryable = queryable.Where(i => i.IsActive == query.IsActive.Value);

        return queryable;
    }

    /// <summary>
    /// 根据 InvoiceQueryDto 过滤销售发票
    /// </summary>
    public static IQueryable<Invoice> Filter(this IQueryable<Invoice> queryable, InvoiceQueryDto query)
    {
        if (query.Status.HasValue)
            queryable = queryable.Where(d => d.Status == query.Status.Value);

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
    /// 根据 BillQueryDto 过滤采购账单
    /// </summary>
    public static IQueryable<Bill> Filter(this IQueryable<Bill> queryable, BillQueryDto query)
    {
        if (query.Status.HasValue)
            queryable = queryable.Where(d => d.Status == query.Status.Value);

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

    /// <summary>
    /// 根据 CreditMemoQueryDto 过滤销售贷项单
    /// </summary>
    public static IQueryable<CreditMemo> Filter(this IQueryable<CreditMemo> queryable, CreditMemoQueryDto query)
    {
        if (query.Status.HasValue)
            queryable = queryable.Where(d => d.Status == query.Status.Value);

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
    /// 根据 ExpenseQueryDto 过滤费用支出
    /// </summary>
    public static IQueryable<Expense> Filter(this IQueryable<Expense> queryable, ExpenseQueryDto query)
    {
        if (query.Status.HasValue)
            queryable = queryable.Where(d => d.Status == query.Status.Value);

        if (query.VendorId.HasValue)
            queryable = queryable.Where(d => d.VendorId == query.VendorId.Value);

        if (!string.IsNullOrWhiteSpace(query.PaymentMethod))
            queryable = queryable.Where(d => d.PaymentMethod == query.PaymentMethod);

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
    /// 根据 PaymentEntryQueryDto 过滤收付款单
    /// </summary>
    public static IQueryable<PaymentEntry> Filter(this IQueryable<PaymentEntry> queryable, PaymentEntryQueryDto query)
    {
        if (query.Status.HasValue)
            queryable = queryable.Where(d => d.Status == query.Status.Value);

        if (query.Direction.HasValue)
            queryable = queryable.Where(d => d.Direction == query.Direction.Value);

        if (!string.IsNullOrWhiteSpace(query.PaymentMethod))
            queryable = queryable.Where(d => d.PaymentMethod == query.PaymentMethod);

        if (query.PartyId.HasValue)
            queryable = queryable.Where(d => d.PartyId == query.PartyId.Value);

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
                (d.Reference != null && d.Reference.ToLower().Contains(keyword)) ||
                (d.Memo != null && d.Memo.ToLower().Contains(keyword)));
        }

        return queryable;
    }

    /// <summary>
    /// 资金划转单查询过滤
    /// </summary>
    public static IQueryable<Transfer> Filter(this IQueryable<Transfer> queryable, TransferQueryDto query)
    {
        if (query.Status.HasValue)
            queryable = queryable.Where(t => t.Status == query.Status.Value);

        if (query.AccountId.HasValue)
            queryable = queryable.Where(t => t.FromAccountId == query.AccountId.Value || t.ToAccountId == query.AccountId.Value);

        if (query.From.HasValue)
        {
            var from = query.From.Value.ToUtcDate();
            queryable = queryable.Where(t => t.TransferDate >= from);
        }

        if (query.To.HasValue)
        {
            var toExclusive = query.To.Value.ToUtcDate().AddDays(1);
            queryable = queryable.Where(t => t.TransferDate < toExclusive);
        }

        return queryable;
    }

    /// <summary>
    /// 银行对账查询过滤
    /// </summary>
    public static IQueryable<Reconciliation> Filter(this IQueryable<Reconciliation> queryable, ReconciliationQueryDto query)
    {
        if (query.AccountId.HasValue)
            queryable = queryable.Where(r => r.AccountId == query.AccountId.Value);

        if (query.Status.HasValue)
            queryable = queryable.Where(r => r.Status == query.Status.Value);

        return queryable;
    }

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
