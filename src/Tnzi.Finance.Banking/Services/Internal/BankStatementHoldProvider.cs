namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// 银行域对 <see cref="IJournalLineHoldProvider"/> 的实现：一条 <c>Matched</c> 的导入流水
/// 持有它所指向的那条总账行。
/// </summary>
/// <remarks>
/// 这是把"会计内核 → 银行域"的反向依赖翻转过来的那一半：内核只认 <see cref="IJournalLineHoldProvider"/>，
/// 由本类把 <c>BankTransaction</c> 的事实翻译成内核听得懂的话。银行域随后可整体搬进
/// <c>Tnzi.Finance.Banking</c> 而内核一行不改。
/// <br/><br/>
/// 拒绝语与原先内联在 <c>ReversalGuard</c> 里的措辞逐字保留（有测试断言"查询说的"与"真冲销
/// 收到的"逐字相等，两边共用本类即天然同源）。
/// </remarks>
public class BankStatementHoldProvider : IJournalLineHoldProvider
{
    private readonly IReadOnlyRepository<BankTransaction, Guid> _txnRepository;

    public BankStatementHoldProvider(IReadOnlyRepository<BankTransaction, Guid> txnRepository)
    {
        _txnRepository = Check.NotNull(txnRepository);
    }

    public async Task<IReadOnlyList<JournalLineHold>> GetHoldsAsync(
        IReadOnlyCollection<Guid> journalLineIds,
        CancellationToken cancellationToken = default)
    {
        if (journalLineIds == null || journalLineIds.Count == 0)
            return Array.Empty<JournalLineHold>();

        // 入参有界（一张凭证的行 / 一页对账候选），故一条 IN 查询即可；反过来把全部
        // Matched 流水物化出来再回填是不行的——那个集合随经营年限只增不减。
        var rows = await _txnRepository.AsNoTracking()
            .Where(t => t.Status == BankTransactionStatus.Matched
                        && t.MatchedJournalLineId != null
                        && journalLineIds.Contains(t.MatchedJournalLineId!.Value))
            .OrderBy(t => t.TxnDate)
            .Select(t => new { LineId = t.MatchedJournalLineId!.Value, t.TxnDate, t.Amount, t.Reference })
            .ToListAsync(cancellationToken);

        return rows.Select(t =>
        {
            var reference = string.IsNullOrWhiteSpace(t.Reference) ? string.Empty : $", reference {t.Reference}";
            return new JournalLineHold(
                t.LineId,
                ReversalBlockReasons.StatementMatched,
                $"These lines are matched to imported bank statement lines (dated {t.TxnDate:yyyy-MM-dd}, "
                + $"amount {t.Amount.ToString(CultureInfo.InvariantCulture)}{reference}). "
                + "Unmatch them first, then reverse.");
        }).ToList();
    }
}
