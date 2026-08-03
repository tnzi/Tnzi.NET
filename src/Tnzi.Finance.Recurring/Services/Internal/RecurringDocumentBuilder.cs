namespace Tnzi.Finance.Recurring.Services.Internal;

/// <summary>
/// 把一条模板 + 一个期次日期变成一张真单据
/// </summary>
/// <remarks>
/// public 因经 DI 注入（沿 <c>BankDocumentDrafter</c> / <c>OfferComposer</c> 先例）。
///
/// **一律委托既有的 <c>CreateDraftAsync</c>**，绝不自己拼凭证：税、汇率、往来方账期、
/// 科目回退这些规则已经在单据服务里，重写一遍等于让同一张发票按谁生成的而算出
/// 两个金额。
/// </remarks>
public class RecurringDocumentBuilder
{
    private readonly IInvoiceService _invoiceService;
    private readonly IBillService _billService;
    private readonly IExpenseService _expenseService;

    public RecurringDocumentBuilder(
        IInvoiceService invoiceService,
        IBillService billService,
        IExpenseService expenseService)
    {
        _invoiceService = Check.NotNull(invoiceService);
        _billService = Check.NotNull(billService);
        _expenseService = Check.NotNull(expenseService);
    }

    /// <summary>生成结果：单据类型令牌 + Id + 编号（已过账才有）</summary>
    public sealed record GeneratedDocument(string DocType, Guid DocId, string? Number, bool Posted);

    /// <summary>
    /// 按模板造一张单据。
    /// </summary>
    /// <param name="template">模板（含行）</param>
    /// <param name="periodDate">期次日期 = 单据日</param>
    /// <param name="autoPost">生成后是否过账</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task<Result<GeneratedDocument>> BuildAsync(
        RecurringDocument template, DateTime periodDate, bool autoPost, CancellationToken cancellationToken)
    {
        Check.NotNull(template);

        var lines = template.Lines.OrderBy(l => l.LineNumber).ToList();
        if (lines.Count == 0)
            return Result<GeneratedDocument>.Failure("The template has no lines.", 400);

        var docDate = periodDate.ToUtcDate();
        var dueDate = template.DueDays.HasValue ? docDate.AddDays(template.DueDays.Value) : (DateTime?)null;

        return template.Kind switch
        {
            RecurringDocKind.Invoice => await BuildInvoiceAsync(template, lines, docDate, dueDate, autoPost, cancellationToken),
            RecurringDocKind.Bill => await BuildBillAsync(template, lines, docDate, dueDate, autoPost, cancellationToken),
            RecurringDocKind.Expense => await BuildExpenseAsync(template, lines, docDate, autoPost, cancellationToken),
            _ => Result<GeneratedDocument>.Failure($"Unsupported recurring document kind '{template.Kind}'.", 400),
        };
    }

    private async Task<Result<GeneratedDocument>> BuildInvoiceAsync(
        RecurringDocument t, List<RecurringLine> lines, DateTime docDate, DateTime? dueDate, bool autoPost, CancellationToken ct)
    {
        var draft = await _invoiceService.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = t.PartyId,
            DocDate = docDate,
            DueDate = dueDate,
            Currency = t.Currency,
            Memo = t.Memo,
            Lines = [.. lines.Select(l => new CreateInvoiceLineDto
            {
                ItemId = l.ItemId,
                Description = l.Description,
                AccountId = l.AccountId,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                TaxCodeId = l.TaxCodeId,
            })],
        }, ct);
        if (!draft.Succeeded)
            return Result<GeneratedDocument>.Failure(draft.Message!, draft.Code ?? 400);

        if (!autoPost)
            return Result<GeneratedDocument>.Success(new GeneratedDocument(FinanceSourceTypes.Invoice, draft.Data!.Id, draft.Data.Number, false));

        var posted = await _invoiceService.PostAsync(draft.Data!.Id, ct);
        return posted.Succeeded
            ? Result<GeneratedDocument>.Success(new GeneratedDocument(FinanceSourceTypes.Invoice, posted.Data!.Id, posted.Data.Number, true))
            : Result<GeneratedDocument>.Failure(posted.Message!, posted.Code ?? 400);
    }

    private async Task<Result<GeneratedDocument>> BuildBillAsync(
        RecurringDocument t, List<RecurringLine> lines, DateTime docDate, DateTime? dueDate, bool autoPost, CancellationToken ct)
    {
        var draft = await _billService.CreateDraftAsync(new CreateBillDto
        {
            VendorId = t.PartyId,
            DocDate = docDate,
            DueDate = dueDate,
            Currency = t.Currency,
            Memo = t.Memo,
            Lines = [.. lines.Select(l => new CreateBillLineDto
            {
                ItemId = l.ItemId,
                Description = l.Description,
                AccountId = l.AccountId,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                TaxCodeId = l.TaxCodeId,
            })],
        }, ct);
        if (!draft.Succeeded)
            return Result<GeneratedDocument>.Failure(draft.Message!, draft.Code ?? 400);

        if (!autoPost)
            return Result<GeneratedDocument>.Success(new GeneratedDocument(FinanceSourceTypes.Bill, draft.Data!.Id, draft.Data.Number, false));

        var posted = await _billService.PostAsync(draft.Data!.Id, ct);
        return posted.Succeeded
            ? Result<GeneratedDocument>.Success(new GeneratedDocument(FinanceSourceTypes.Bill, posted.Data!.Id, posted.Data.Number, true))
            : Result<GeneratedDocument>.Failure(posted.Message!, posted.Code ?? 400);
    }

    /// <summary>
    /// 费用单。
    /// </summary>
    /// <remarks>
    /// 费用行按**金额**而不是数量×单价（单据本身就是这么设计的），故模板行的
    /// 两者在这里相乘落成 Amount —— 数量在费用语境里没有承载对象。
    /// </remarks>
    private async Task<Result<GeneratedDocument>> BuildExpenseAsync(
        RecurringDocument t, List<RecurringLine> lines, DateTime docDate, bool autoPost, CancellationToken ct)
    {
        if (t.PaidFromAccountId is null)
            return Result<GeneratedDocument>.Failure("The template has no account to pay from.", 400);

        var missingAccount = lines.FirstOrDefault(l => l.AccountId is null);
        if (missingAccount != null)
            return Result<GeneratedDocument>.Failure($"Line {missingAccount.LineNumber} has no expense account.", 400);

        var draft = await _expenseService.CreateDraftAsync(new CreateExpenseDto
        {
            VendorId = t.PartyId == Guid.Empty ? null : t.PartyId,
            PaidFromAccountId = t.PaidFromAccountId.Value,
            PaymentMethod = t.PaymentMethod,
            DocDate = docDate,
            Currency = t.Currency,
            Memo = t.Memo,
            Lines = [.. lines.Select(l => new CreateExpenseLineDto
            {
                Description = l.Description,
                AccountId = l.AccountId!.Value,
                Amount = l.Quantity * l.UnitPrice,
                TaxCodeId = l.TaxCodeId,
            })],
        }, ct);
        if (!draft.Succeeded)
            return Result<GeneratedDocument>.Failure(draft.Message!, draft.Code ?? 400);

        if (!autoPost)
            return Result<GeneratedDocument>.Success(new GeneratedDocument(FinanceSourceTypes.Expense, draft.Data!.Id, draft.Data.Number, false));

        var posted = await _expenseService.PostAsync(draft.Data!.Id, ct);
        return posted.Succeeded
            ? Result<GeneratedDocument>.Success(new GeneratedDocument(FinanceSourceTypes.Expense, posted.Data!.Id, posted.Data.Number, true))
            : Result<GeneratedDocument>.Failure(posted.Message!, posted.Code ?? 400);
    }
}
