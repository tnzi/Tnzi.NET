namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// CPA-005 文件组装（1464 字符逻辑记录，A/C/Z，仅 credit）
/// </summary>
/// <remarks>
/// A(header) + 每笔一条 C(credit) + Z(trailer)。C 记录含 6 个 240 字符交易段，本实现每记录用第 1 段、
/// 其余 5 段空白填充（保持 1464 定长，语义上等价单笔一记录）。纯函数、确定性。
/// <para>
/// ★ 字段位置按 CPA Standard 005 逐位排布，测试按位置逐字段断言（不是拿本实现自己的输出当金标准）：
/// A = 记录号 / Originator ID / 文件序号 / 创建日 / 数据中心 / 保留区 / 币种(56-58)；
/// C 段 = 交易类型 / 金额 / 到账日 / 收款行 / 收款账号 / 跟踪号 / 存储交易类型 / 付款方简称 / 收款人名 / 付款方全称 /
/// 直接清算方用户 ID / 付款方交叉参考号 / 退票行 / 退票账号 / 附言 / 填充 / 结算码 / 无效数据元素 ID；
/// Z = 借方合计与笔数在前（25-46）、贷方合计与笔数在后（47-68）、再两组错误更正合计。
/// 此前 C 段在付款方全称之后多塞了一组出款行 / 出款账号，退票行与退票账号整体前移 8 位；
/// Z 把贷方合计写进了借方的位置，银行要么按合计对不上拒收，要么收下而退票无处可退。
/// </para>
/// 各金融机构的方言差异（数据中心号、直接清算方用户 ID、段内可选字段、多段打包）以可替换 composer 覆盖，
/// 落地前须核对银行样件。PAD debit 段列为 backlog（本版仅 credit）。
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

        // 币种进 A 记录 56-58：装批阶段已限定 CPA-005 = CAD，这里只拒绝装不进三位字母代码的值，
        // 而不是把它截断或留空（留空的币种码同样是银行拒收的理由）。
        var currency = (request.Currency ?? string.Empty).Trim().ToUpperInvariant();
        if (currency.Length != 3 || !currency.All(c => c is >= 'A' and <= 'Z'))
            throw new BusinessException("A CPA-005 file requires a 3-letter currency code.");

        // A 记录（header）
        records.Add(HeaderRecord(request, originatorId, fcn, recordCount, currency));

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

            // 段内位置（1 起）在右侧注释；记录位置 = 段内位置 + 24。
            var segment =
                CreditTransactionType +                                   // 1-3     Transaction Type (3)
                EftFieldWriter.Amount(cents, 10, $"payee '{entry.PayeeName}'") + // 4-13 Amount (10)
                EftFieldWriter.Julian(request.EffectiveDate) +           // 14-19   Date Funds to be Available (6)
                payeeTransit +                                            // 20-28   Institutional Identification No. (9)
                EftFieldWriter.AccountField(entry.AccountNumber, AccountNumberWidth, $"payee '{entry.PayeeName}'") + // 29-40 Payee Account No. (12)
                itemTrace +                                               // 41-62   Item Trace No. (22)
                "000" +                                                   // 63-65   Stored Transaction Type (3)
                shortName +                                               // 66-80   Originator's Short Name (15)
                EftFieldWriter.Text(entry.PayeeName, 30) +              // 81-110  Payee Name (30)
                longName +                                                // 111-140 Originator's Long Name (30)
                EftFieldWriter.Spaces(10) +                              // 141-150 Originating Direct Clearer's User's ID (10)，空白（银行方言另行覆盖）
                EftFieldWriter.Text(entry.Reference, 19) +              // 151-169 Originator's Cross Reference No. (19)
                originTransit +                                           // 170-178 Institutional ID No. for Returns (9)
                originAccount +                                           // 179-190 Account No. for Returns (12)
                EftFieldWriter.Spaces(15) +                              // 191-205 Originator's Sundry Information (15)
                EftFieldWriter.Spaces(22) +                              // 206-227 Filler (22)
                EftFieldWriter.Spaces(2) +                               // 228-229 Originator-Direct Clearer Settlement Code (2)
                EftFieldWriter.Num(0, 11);                               // 230-240 Invalid Data Element I.D. (11)
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

    private static string HeaderRecord(EftComposeRequest r, string originatorId, string fcn, long recordCount, string currency)
    {
        var record =
            "A" +                                         // 1       Logical Record Type ID
            EftFieldWriter.Num(recordCount, 9) +          // 2-10    Logical Record Count
            originatorId +                                // 11-20   Originator's ID
            fcn +                                         // 21-24   File Creation No.
            EftFieldWriter.Julian(r.CreationTime) +      // 25-30   Creation Date (6)
            EftFieldWriter.Num(0, 5) +                    // 31-35   Destination Data Centre (5)
            EftFieldWriter.Spaces(20) +                   // 36-55   Reserved Customer-Direct Clearer Communication Area (20)
            currency +                                    // 56-58   Currency Code Identifier (3)
            EftFieldWriter.Spaces(Width - 58);            // 59-1464 Filler
        return EftFieldWriter.Fixed(record, Width);
    }

    private static string TrailerRecord(string originatorId, string fcn, long recordCount, long totalCents, int creditCount)
    {
        var record =
            "Z" +                                         // 1       Logical Record Type ID
            EftFieldWriter.Num(recordCount, 9) +          // 2-10    Logical Record Count
            originatorId +                                // 11-20   Originator's ID
            fcn +                                         // 21-24   File Creation No.
            EftFieldWriter.Num(0, 14) +                   // 25-38   Total Value of Debit Transactions (14)
            EftFieldWriter.Num(0, 8) +                    // 39-46   Total Number of Debit Transactions (8)
            EftFieldWriter.Amount(totalCents, 14, "the file total") + // 47-60 Total Value of Credit Transactions (14)
            EftFieldWriter.Num(creditCount, 8) +          // 61-68   Total Number of Credit Transactions (8)
            EftFieldWriter.Num(0, 14) +                   // 69-82   Total Value of Error Corrections "E" (14)
            EftFieldWriter.Num(0, 8) +                    // 83-90   Total Number of Error Corrections "E" (8)
            EftFieldWriter.Num(0, 14) +                   // 91-104  Total Value of Error Corrections "F" (14)
            EftFieldWriter.Num(0, 8) +                    // 105-112 Total Number of Error Corrections "F" (8)
            EftFieldWriter.Spaces(Width - 112);           // 113-1464 Filler
        return EftFieldWriter.Fixed(record, Width);
    }
}
