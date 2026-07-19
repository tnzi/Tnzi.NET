namespace Tnzi.Finance.Services.Internal;

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
    private const string CreditTransactionType = "450"; // 直存/一般 credit

    public static string Build(EftComposeRequest request)
    {
        Check.NotNull(request);
        if (request.Entries.Count == 0)
            throw new BusinessException("A CPA-005 batch requires at least one entry.");

        var originatorId = EftFieldWriter.Text(request.OriginatorId, 10);
        var fcn = EftFieldWriter.Num(request.FileCreationNumber, 4);
        // CPA-005 电子路由号格式 = "0" + 机构号(3) + 分行号(5)（机构在前），与纸质支票 MICR 的
        // 分行-机构顺序（见 MicrLineComposer CA 分支）相反。定长错位会导致整个文件被接收行拒收。
        var originTransit = "0" + EftFieldWriter.Digits(request.OriginatorInstitutionNumber, 3) + EftFieldWriter.Digits(request.OriginatorTransitNumber, 5);
        var originAccount = EftFieldWriter.Text(request.OriginatorAccountNumber, 12);
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

            var payeeTransit = "0" + EftFieldWriter.Digits(entry.InstitutionNumber, 3) + EftFieldWriter.Digits(entry.TransitNumber, 5);
            var itemTrace = originTransit + fcn + EftFieldWriter.Num(seq, 9); // 9 + 4 + 9 = 22

            var segment =
                CreditTransactionType +                                   // Transaction Type (3)
                EftFieldWriter.Num(cents, 10) +                          // Amount (10)
                EftFieldWriter.Julian(request.EffectiveDate) +           // Date Funds Available (6)
                payeeTransit +                                            // Payee Institution/Transit (9)
                EftFieldWriter.Text(entry.AccountNumber, 12) +          // Payee Account Number (12)
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
            EftFieldWriter.Num(totalCents, 14) +          // Total Value of Credit
            EftFieldWriter.Num(creditCount, 8) +          // Total Number of Credit
            EftFieldWriter.Num(0, 14) +                    // Total Value of Debit
            EftFieldWriter.Num(0, 8) +                     // Total Number of Debit
            EftFieldWriter.Spaces(Width - 68);            // Filler
        return EftFieldWriter.Fixed(record, Width);
    }
}
