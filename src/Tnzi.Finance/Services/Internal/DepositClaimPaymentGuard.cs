namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 拒绝作废「已经被某张存活存款单收走」的收款单。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么必须有这道门</b>：存款单过账后，待存款项科目上那笔钱已经被贷记掉、换成了银行余额。
/// 此时若把当初那张收款单作废，冲销凭证会把它的借方（待存款项）反过来贷一次 ——
/// <b>同一笔钱被贷了两次</b>，待存款项科目就此出现一个谁也解释不了的负数，
/// 而每一张凭证自身都是平的、试算平衡恒为零，没有任何报表会因此报警。
/// </para>
/// <para>
/// <b>草稿存款单也拦</b>：草稿持有的声明让那张收款从候选清单里消失了。放行作废，
/// 草稿就成了一张「装着一笔已作废收款」的存款单，等到过账那一刻才失败 ——
/// 而那时人已经跑完一趟银行。两种状态的补救路径也一样（把它从存款单里移出去，
/// 或者作废/删掉那张存款单），所以规则统一，只按状态区分提示语。
/// </para>
/// <para>
/// 与 <c>EftBatchPaymentGuard</c>（银行域，拦「还在未作废 EFT 批次里」的付款）同构；
/// 区别只是本守卫的两端都在 Finance 核心内，因此直接注册在 <c>FinanceModule</c>。
/// </para>
/// </remarks>
public sealed class DepositClaimPaymentGuard : IFinancePostingGuard
{
    private readonly IReadOnlyRepository<DepositLine, Guid> _lineRepository;
    private readonly IReadOnlyRepository<Deposit, Guid> _depositRepository;

    public DepositClaimPaymentGuard(
        IReadOnlyRepository<DepositLine, Guid> lineRepository,
        IReadOnlyRepository<Deposit, Guid> depositRepository)
    {
        _lineRepository = Check.NotNull(lineRepository);
        _depositRepository = Check.NotNull(depositRepository);
    }

    /// <inheritdoc />
    public async Task<Result> CheckAsync(FinancePostingGuardContext context, CancellationToken cancellationToken = default)
    {
        Check.NotNull(context);

        // 只管收款单的撤销类操作；过账与其它单据类型零开销放行。
        if (context.Operation is not (FinancePostingOperation.Void or FinancePostingOperation.Reverse))
            return Result.Success();
        if (!string.Equals(context.DocType, FinanceSourceTypes.PaymentEntry, StringComparison.Ordinal))
            return Result.Success();
        if (!Guid.TryParse(context.DocId, out var paymentId))
            return Result.Success();

        // 声明在存款单作废时被置空，所以「查得到声明」就等于「存款单仍然存活」。
        var line = await _lineRepository.AsNoTracking()
            .FirstOrDefaultAsync(l => l.ClaimedPaymentEntryId == paymentId, cancellationToken);
        if (line == null)
            return Result.Success();

        var deposit = await _depositRepository.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == line.DepositId, cancellationToken);
        if (deposit == null || deposit.Status == FinanceDocumentStatus.Voided)
            return Result.Success();

        var label = string.IsNullOrWhiteSpace(deposit.Number) ? "a draft deposit" : $"deposit {deposit.Number}";

        return deposit.Status == FinanceDocumentStatus.Draft
            ? Result.Failure(
                $"This receipt is on {label}. Remove it from that deposit (or delete the deposit) first, then void the receipt.",
                409)
            : Result.Failure(
                $"This receipt was already banked by {label}. Void that deposit first, then void the receipt.",
                409);
    }
}
