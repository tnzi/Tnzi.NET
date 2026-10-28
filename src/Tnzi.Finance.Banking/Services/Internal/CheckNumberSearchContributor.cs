namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// 银行域对 <see cref="IGeneralLedgerSearchContributor"/> 的实现：按**已开具支票的支票号**
/// 命中它所支付的那张付款单。
/// </summary>
/// <remarks>
/// 只认 <c>Issued</c> 的票：作废票与毁票占号留痕，但它们不代表一笔仍然成立的付款，
/// 把它们搜出来会把已止付的票号指向一笔活的付款（既有测试用「作废票 7799 对照已开具票 7788」
/// 锁定这条规则）。
/// <para>
/// ★ 纯数字的关键字先去掉前导零：票面与 MICR 上印的是补零后的号（<see cref="CheckNumberFormat"/>），
/// 操作员照着纸或对账单输入的「01001」要找到库里的 1001。
/// </para>
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

        var digits = NormalizeNumericKeyword(keyword);
        var paymentIds = await _checkRepository.AsNoTracking()
            .Where(c => c.Status == CheckStatus.Issued
                        && c.PaymentEntryId != null
                        && c.CheckNumber.ToString().Contains(digits))
            .Select(c => c.PaymentEntryId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Guid → string 的转换刻意放 .NET 侧：SourceId 写入用的是 .NET "D" 格式，
        // 交给各库自己文本化会因格式/大小写不一致而静默失配。
        return paymentIds
            .Select(id => new GeneralLedgerSourceMatch(FinanceSourceTypes.PaymentEntry, id.ToString()))
            .ToList();
    }

    /// <summary>全是 ASCII 数字的关键字去掉前导零（全是零时留一个），其余原样。</summary>
    private static string NormalizeNumericKeyword(string keyword)
    {
        var trimmed = keyword.Trim();
        if (trimmed.Length == 0 || !trimmed.All(char.IsAsciiDigit))
            return keyword;
        var withoutZeros = trimmed.TrimStart('0');
        return withoutZeros.Length == 0 ? "0" : withoutZeros;
    }
}
