namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// CPA-005 文件组装（1464 字符逻辑记录，A/C/Z，仅 credit）
/// </summary>
/// <remarks>
/// A(header) + 每笔一条 C(credit) + Z(trailer)。C 记录含 6 个 240 字符交易段，本实现每记录用第 1 段、
/// 其余 5 段空白填充（保持 1464 定长，语义上等价单笔一记录）。纯函数、确定性——黄金文件测试锁定。
/// 各金融机构占位差异（数据中心号、段内可选字段、多段打包）以可替换 composer 覆盖，落地前须核对样件。
/// PAD debit 段列为 backlog（本版仅 credit）。
/// </remarks>
internal static class Cpa005FileBuilder
{
    private const int Width = 1464;
    private const int SegmentWidth = 240;

    /// <summary>Payee / Originator Account Number 字段宽度。
    /// <see cref="BankNumberHelper.MaxAccountNumberLength"/> 直接引用它当录入上限（理由同 NACHA 侧）。</summary>
    internal const int AccountNumberWidth = 12;

    /// <summary>Originator ID 字段宽度（A/C/Z 记录）。<see cref="BankNumberHelper.MaxEftOriginatorIdLength"/>
    /// 直接引用它当录入上限（理由同账号宽度）。</summary>
    internal const int OriginatorIdWidth = 10;
    private const string CreditTransactionType = "450"; // 直存/一般 credit

    public static string Build(EftComposeRequest request)
    {
        Check.NotNull(request);
        if (request.Entries.Count == 0)
            throw new BusinessException("A CPA-005 batch requires at least one entry.");

        // 出款方路由缺失 → 整份文件的 originating transit 会被补成九个零：一份语法合法、
        // 指向不存在机构的报文。NACHA 侧对 ODFI 路由一直是 fail-fast 的（见 NachaFileBuilder），
        // 这一侧没有理由更宽松。★ 档案本身允许留空路由（只登记名称/账号是合法的），
        // 所以判据不能挪到 ValidateRouting 上，只能在「要出文件了」这一刻问。
        if (!BankNumberHelper.HasTransferRouting(BankNumberScheme.CaEft, null, request.OriginatorInstitutionNumber, request.OriginatorTransitNumber))
            throw new BusinessException("A CPA-005 file requires the originator's 3-digit institution number and 5-digit transit number.");

        // 标识符不截断：截出来的是另一个语法合法的 originator id（与账号同一类失效）
        var originatorId = EftFieldWriter.IdentifierField(request.OriginatorId, OriginatorIdWidth, "CPA-005 originator id");
        var fcn = EftFieldWriter.Num(request.FileCreationNumber, 4);
        // CPA-005 电子路由号格式 = "0" + 机构号(3) + 分行号(5)（机构在前），与纸质支票 MICR 的
        // 分行-机构顺序（见 MicrLineComposer CA 分支）相反。定长错位会导致整个文件被接收行拒收。
        var originTransit = "0" + EftFieldWriter.Digits(request.OriginatorInstitutionNumber, 3) + EftFieldWriter.Digits(request.OriginatorTransitNumber, 5);
        // 出款方账号为空 → 定宽写入器会补成 12 个空格：一份语法合法、Originator / Return Account 都空白的报文。
        if (string.IsNullOrWhiteSpace(request.OriginatorAccountNumber))
            throw new BusinessException("A CPA-005 file requires the originating account number; the bank account profile has none on file (or it cannot be decrypted).");
        var originAccount = EftFieldWriter.AccountField(request.OriginatorAccountNumber, AccountNumberWidth, "the originating account");
        var shortName = EftFieldWriter.Text(request.OriginatorName, 15);
        var longName = EftFieldWriter.Text(request.OriginatorName, 30);

        var records = new List<string>();
        long recordCount = 1;

        // A 记录（header）
        records.Add(HeaderRecord(request, originatorId, fcn, recordCount));

        long totalCents = 0;
        var seq = 1;
        foreach (var entry in request.Entries)
        {
            recordCount++;
            var cents = EftFieldWriter.Cents(entry.Amount);
            totalCents += cents;

            // 收款方路由缺失同样补成九个零 —— 那笔钱要么退回，要么由接收行按一个不是它的
            // 路由处理。与 NACHA 侧「RDFI 必须正好 9 位」的检查对称。
            if (!BankNumberHelper.HasTransferRouting(BankNumberScheme.CaEft, null, entry.InstitutionNumber, entry.TransitNumber))
                throw new BusinessException($"Payee '{entry.PayeeName}' has an incomplete Canadian routing number (3-digit institution plus 5-digit transit) for CPA-005.");

            var payeeTransit = "0" + EftFieldWriter.Digits(entry.InstitutionNumber, 3) + EftFieldWriter.Digits(entry.TransitNumber, 5);
            var itemTrace = originTransit + fcn + EftFieldWriter.Num(seq, 9); // 9 + 4 + 9 = 22

            var segment =
                CreditTransactionType +                                   // Transaction Type (3)
                EftFieldWriter.Amount(cents, 10, $"payee '{entry.PayeeName}'") + // Amount (10)
                EftFieldWriter.Julian(request.EffectiveDate) +           // Date Funds Available (6)
                payeeTransit +                                            // Payee Institution/Transit (9)
                EftFieldWriter.AccountField(entry.AccountNumber, AccountNumberWidth, $"payee '{entry.PayeeName}'") + // Payee Account Number (12)
                itemTrace +                                               // Item Trace Number (22)
                "000" +                                                   // Stored Transaction Type (3)
                shortName +                                               // Originator Short Name (15)
                EftFieldWriter.Text(entry.PayeeName, 30) +              // Payee Name (30)
                longName +                                                // Originator Long Name (30)
                originTransit +                                           // Originating Institution/Transit (9)
                originAccount +                                           // Originator Account Number (12)
                originTransit +                                           // Return Institution/Transit (9)
                originAccount +                                           // Return Account Number (12)
                EftFieldWriter.Spaces(58);                               // Filler → 240
            EftFieldWriter.Fixed(segment, SegmentWidth);

            var record =
                "C" +
                EftFieldWriter.Num(recordCount, 9) +
                originatorId +
                fcn +
                segment +
                string.Concat(Enumerable.Repeat(EftFieldWriter.Spaces(SegmentWidth), 5)); // 段 2-6 空白
            records.Add(EftFieldWriter.Fixed(record, Width));
            seq++;
        }

        recordCount++;
        records.Add(TrailerRecord(originatorId, fcn, recordCount, totalCents, request.Entries.Count));

        return string.Join("\n", records);
    }

    private static string HeaderRecord(EftComposeRequest r, string originatorId, string fcn, long recordCount)
    {
        var record =
            "A" +
            EftFieldWriter.Num(recordCount, 9) +
            originatorId +
            fcn +
            EftFieldWriter.Julian(r.CreationTime) +      // Creation Date (6)
            EftFieldWriter.Num(0, 5) +                    // Destination Data Centre (5)
            EftFieldWriter.Spaces(Width - 35);            // Filler
        return EftFieldWriter.Fixed(record, Width);
    }

    private static string TrailerRecord(string originatorId, string fcn, long recordCount, long totalCents, int creditCount)
    {
        var record =
            "Z" +
            EftFieldWriter.Num(recordCount, 9) +
            originatorId +
            fcn +
            EftFieldWriter.Amount(totalCents, 14, "the file total") + // Total Value of Credit
            EftFieldWriter.Num(creditCount, 8) +          // Total Number of Credit
            EftFieldWriter.Num(0, 14) +                    // Total Value of Debit
            EftFieldWriter.Num(0, 8) +                     // Total Number of Debit
            EftFieldWriter.Spaces(Width - 68);            // Filler
        return EftFieldWriter.Fixed(record, Width);
    }
}
