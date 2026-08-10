namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// 拒绝作废「还在某个未作废 EFT 批次里」的付款单。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么必须有这道门</b>：<c>PaymentEntryService.VoidAsync</c> 在冲销之前会调
/// <c>IFinancePostingGuard</c>，但在此之前<b>全仓一个实现都没有</b> ——
/// 空集合上的 <c>CheckAsync</c> 恒成功。而 Finance 核心对 <c>EftBatch</c> 的引用数是 <b>0</b>
/// （这是刻意的依赖方向），<c>BankStatementHoldProvider</c> 又只认「已匹配的银行流水」，
/// 与 EFT 无关。于是<b>已经生成并交给银行的付款可以被静默作废</b>：
/// 账上冲销掉了，银行按生效日照付，批次里仍列着它、合计仍含它，没有任何地方对得上。
/// </para>
/// <para>
/// 支票侧一直有对称的保护（<c>PaymentVoidedCheckHandler</c> 联动作废票据、登记簿留 Void 痕），
/// EFT 侧此前一片空白 —— 而 EFT 是真正把钱送出门且不可撤回的那一条。
/// </para>
/// <para>
/// <b>为什么 Draft 批次也拦</b>：<c>GenerateAsync</c> 只校验批次状态，<b>不重新校验行上付款的状态</b>。
/// 放行 Draft 就意味着「作废付款 → 生成文件」会产出一份支付已作废付款的报文，同样是真金白银。
/// 两种状态的补救路径本来也一样（本模块没有「移除单行」的操作，只能 <c>VoidBatchAsync</c>
/// 整批作废后重建），所以规则统一成「批次未作废即拒绝」，只按状态区分提示语。
/// </para>
/// <para>
/// <b>补救是显式的</b>：先作废批次 —— 那是操作员在明确声明「这批没有发出去」，
/// 是一个有主体、有痕迹的动作，而不是作废一笔付款时悄悄发生的副作用。
/// </para>
/// <para>
/// 依赖方向与 <c>IJournalLineHoldProvider</c> / <c>IGeneralLedgerSearchContributor</c> 一致：
/// 核心提问，银行域回答；核心永不反向引用本模块。
/// </para>
/// </remarks>
public sealed class EftBatchPaymentGuard : IFinancePostingGuard
{
    private readonly IReadOnlyRepository<EftBatchLine, Guid> _lineRepository;
    private readonly IReadOnlyRepository<EftBatch, Guid> _batchRepository;

    public EftBatchPaymentGuard(
        IReadOnlyRepository<EftBatchLine, Guid> lineRepository,
        IReadOnlyRepository<EftBatch, Guid> batchRepository)
    {
        _lineRepository = Check.NotNull(lineRepository);
        _batchRepository = Check.NotNull(batchRepository);
    }

    /// <inheritdoc />
    public async Task<Result> CheckAsync(FinancePostingGuardContext context, CancellationToken cancellationToken = default)
    {
        Check.NotNull(context);

        // 只管付款单的撤销类操作；过账与其它单据类型零开销放行。
        if (context.Operation is not (FinancePostingOperation.Void or FinancePostingOperation.Reverse))
            return Result.Success();
        if (!string.Equals(context.DocType, FinanceSourceTypes.PaymentEntry, StringComparison.Ordinal))
            return Result.Success();
        if (!Guid.TryParse(context.DocId, out var paymentId))
            return Result.Success();

        // 一笔付款至多在一个存活批次内（EftBatchLine 的 (TenantId, PaymentEntryId) 唯一索引），
        // 作废批次时行是硬删的，所以查到行即意味着批次仍然存活。
        var line = await _lineRepository.AsNoTracking()
            .FirstOrDefaultAsync(l => l.PaymentEntryId == paymentId, cancellationToken);
        if (line == null)
            return Result.Success();

        var batch = await _batchRepository.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == line.EftBatchId, cancellationToken);
        if (batch == null || batch.Status == EftBatchStatus.Voided)
            return Result.Success();

        var label = string.IsNullOrWhiteSpace(batch.Number) ? "a draft EFT batch" : $"EFT batch {batch.Number}";

        return batch.Status == EftBatchStatus.Generated
            ? Result.Failure(
                $"This payment is in {label}, whose file has already been generated and may have been submitted to the bank. "
                + "Void that batch first if it was never submitted, then void the payment.",
                409)
            : Result.Failure(
                $"This payment is in {label}. Void that batch first, then void the payment.",
                409);
    }
}
