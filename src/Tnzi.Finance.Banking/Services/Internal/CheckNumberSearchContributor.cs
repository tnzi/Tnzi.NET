namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// 银行域对 <see cref="IGeneralLedgerSearchContributor"/> 的实现：按**已开具支票的支票号**
/// 命中它所支付的那张付款单。
/// </summary>
/// <remarks>
/// 只认 <c>Issued</c> 的票：作废票与毁票占号留痕，但它们不代表一笔仍然成立的付款，
/// 把它们搜出来会把已止付的票号指向一笔活的付款（既有测试用「作废票 7799 对照已开具票 7788」
/// 锁定这条规则）。
/// </remarks>
public class CheckNumberSearchContributor : IGeneralLedgerSearchContributor
{
    private readonly IReadOnlyRepository<BankCheck, Guid> _checkRepository;

    public CheckNumberSearchContributor(IReadOnlyRepository<BankCheck, Guid> checkRepository)
    {
        _checkRepository = Check.NotNull(checkRepository);
    }

    public async Task<IReadOnlyList<GeneralLedgerSourceMatch>> MatchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return Array.Empty<GeneralLedgerSourceMatch>();

        var paymentIds = await _checkRepository.AsNoTracking()
            .Where(c => c.Status == CheckStatus.Issued
                        && c.PaymentEntryId != null
                        && c.CheckNumber.ToString().Contains(keyword))
            .Select(c => c.PaymentEntryId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Guid → string 的转换刻意放 .NET 侧：SourceId 写入用的是 .NET "D" 格式，
        // 交给各库自己文本化会因格式/大小写不一致而静默失配。
        return paymentIds
            .Select(id => new GeneralLedgerSourceMatch(FinanceSourceTypes.PaymentEntry, id.ToString()))
            .ToList();
    }
}
