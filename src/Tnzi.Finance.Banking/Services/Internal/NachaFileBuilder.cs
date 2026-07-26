namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// NACHA ACH 文件组装（94 字符定长记录，SEC=CCD，仅 credit）
/// </summary>
/// <remarks>
/// File Header(1) + Batch Header(5) + Entry Detail(6, CCD) + Batch Control(8) + File Control(9) + 块填充(9*94)。
/// 纯函数、确定性——黄金文件测试锁定字段布局与汇总。落地前须核对 ODFI 样件（各行差异以可替换 composer 覆盖）。
/// </remarks>
internal static class NachaFileBuilder
{
    private const int Width = 94;
    private const int BlockingFactor = 10;
    private const string EntryClassCode = "CCD";
    private const string ServiceClassCredits = "220"; // ACH credits only

    public static string Build(EftComposeRequest request)
    {
        Check.NotNull(request);
        if (request.Entries.Count == 0)
            throw new BusinessException("A NACHA batch requires at least one entry.");
        if (string.IsNullOrWhiteSpace(request.OriginatorRoutingNumber))
            throw new BusinessException("A NACHA batch requires the originator's 9-digit routing number.");

        var odfi = new string(request.OriginatorRoutingNumber!.Where(char.IsDigit).ToArray());
        if (odfi.Length != 9)
            throw new BusinessException("The originator routing number must be exactly 9 digits for a NACHA file.");
        var odfi8 = odfi[..8];

        // Immediate Origin 字段容 9 位数字（EftFieldWriter.Digits 超长丢高位）；超 9 位则 fail-fast，不静默截断
        var originDigits = new string((request.OriginatorId ?? string.Empty).Where(char.IsDigit).ToArray());
        if (originDigits.Length > 9)
            throw new BusinessException("The originator id has more than 9 digits and would be truncated in the NACHA Immediate Origin field; use a valid 9-digit company id or ODFI routing number.");

        var records = new List<string>
        {
            FileHeader(request, odfi),
            BatchHeader(request, odfi8)
        };

        long totalCredits = 0;
        long entryHash = 0;
        var trace = 1;
        foreach (var entry in request.Entries)
        {
            var rdfi = new string((entry.RoutingNumber ?? string.Empty).Where(char.IsDigit).ToArray());
            if (rdfi.Length != 9)
                throw new BusinessException($"Payee '{entry.PayeeName}' has an invalid US routing number (must be 9 digits) for NACHA.");
            var rdfi8 = rdfi[..8];
            var checkDigit = rdfi[8];
            var cents = EftFieldWriter.Cents(entry.Amount);
            totalCredits += cents;
            entryHash += long.Parse(rdfi8, CultureInfo.InvariantCulture);

            records.Add(EntryDetail(entry, rdfi8, checkDigit, cents, odfi8, trace));
            trace++;
        }

        entryHash %= 10_000_000_000L;
        var entryCount = request.Entries.Count;

        records.Add(BatchControl(request, entryCount, entryHash, totalCredits, odfi8));

        // 块填充：总记录数补到 BlockingFactor 的整数倍
        var baseCount = records.Count + 1; // + File Control
        var paddedCount = (int)(Math.Ceiling(baseCount / (double)BlockingFactor) * BlockingFactor);
        var blockCount = paddedCount / BlockingFactor;

        records.Add(FileControl(entryCount, blockCount, entryHash, totalCredits));

        while (records.Count < paddedCount)
            records.Add(new string('9', Width));

        return string.Join("\n", records);
    }

    private static string FileHeader(EftComposeRequest r, string odfi)
    {
        var modifier = (char)('A' + Math.Abs(r.FileCreationNumber - 1) % 26);
        var record =
            "1" +                                                      // Record Type
            "01" +                                                     // Priority Code
            " " + odfi +                                              // Immediate Destination (space + 9)
            "1" + EftFieldWriter.Digits(r.OriginatorId, 9) +          // Immediate Origin (1 + 9)
            $"{r.CreationTime:yyMMdd}" +                               // File Creation Date
            $"{r.CreationTime:HHmm}" +                                 // File Creation Time
            modifier +                                                 // File ID Modifier
            "094" +                                                    // Record Size
            "10" +                                                     // Blocking Factor
            "1" +                                                      // Format Code
            EftFieldWriter.Text(r.BankName, 23) +                     // Immediate Destination Name
            EftFieldWriter.Text(r.OriginatorName, 23) +              // Immediate Origin Name
            EftFieldWriter.Spaces(8);                                 // Reference Code
        return EftFieldWriter.Fixed(record, Width);
    }

    private static string BatchHeader(EftComposeRequest r, string odfi8)
    {
        var record =
            "5" +                                                      // Record Type
            ServiceClassCredits +                                      // Service Class Code
            EftFieldWriter.Text(r.OriginatorName, 16) +              // Company Name
            EftFieldWriter.Spaces(20) +                               // Company Discretionary Data
            EftFieldWriter.Text(r.OriginatorId, 10) +                // Company Identification
            EntryClassCode +                                          // SEC Code
            EftFieldWriter.Text("PAYMENT", 10) +                     // Company Entry Description
            EftFieldWriter.Spaces(6) +                                // Company Descriptive Date
            $"{r.EffectiveDate:yyMMdd}" +                             // Effective Entry Date
            EftFieldWriter.Spaces(3) +                                // Settlement Date (Julian)
            "1" +                                                      // Originator Status Code
            odfi8 +                                                    // Originating DFI Identification
            EftFieldWriter.Num(1, 7);                                 // Batch Number
        return EftFieldWriter.Fixed(record, Width);
    }

    private static string EntryDetail(EftComposeEntry e, string rdfi8, char checkDigit, long cents, string odfi8, int trace)
    {
        var transactionCode = e.AccountType == BankAccountType.Savings ? "32" : "22"; // savings/checking credit
        var record =
            "6" +                                                      // Record Type
            transactionCode +                                          // Transaction Code
            rdfi8 +                                                    // Receiving DFI Identification
            checkDigit +                                               // Check Digit
            EftFieldWriter.Text(e.AccountNumber, 17) +               // DFI Account Number
            EftFieldWriter.Num(cents, 10) +                          // Amount
            EftFieldWriter.Text(e.Reference, 15) +                   // Individual Identification Number
            EftFieldWriter.Text(e.PayeeName, 22) +                   // Individual Name
            EftFieldWriter.Spaces(2) +                                // Discretionary Data
            "0" +                                                      // Addenda Record Indicator
            odfi8 + EftFieldWriter.Num(trace, 7);                    // Trace Number (8 + 7)
        return EftFieldWriter.Fixed(record, Width);
    }

    private static string BatchControl(EftComposeRequest r, int entryCount, long entryHash, long totalCredits, string odfi8)
    {
        var record =
            "8" +                                                      // Record Type
            ServiceClassCredits +                                      // Service Class Code
            EftFieldWriter.Num(entryCount, 6) +                      // Entry/Addenda Count
            EftFieldWriter.Num(entryHash, 10) +                     // Entry Hash
            EftFieldWriter.Num(0, 12) +                              // Total Debit Amount
            EftFieldWriter.Num(totalCredits, 12) +                  // Total Credit Amount
            EftFieldWriter.Text(r.OriginatorId, 10) +               // Company Identification
            EftFieldWriter.Spaces(19) +                              // Message Authentication Code
            EftFieldWriter.Spaces(6) +                               // Reserved
            odfi8 +                                                   // Originating DFI Identification
            EftFieldWriter.Num(1, 7);                                // Batch Number
        return EftFieldWriter.Fixed(record, Width);
    }

    private static string FileControl(int entryCount, int blockCount, long entryHash, long totalCredits)
    {
        var record =
            "9" +                                                      // Record Type
            EftFieldWriter.Num(1, 6) +                               // Batch Count
            EftFieldWriter.Num(blockCount, 6) +                     // Block Count
            EftFieldWriter.Num(entryCount, 8) +                     // Entry/Addenda Count
            EftFieldWriter.Num(entryHash, 10) +                     // Entry Hash
            EftFieldWriter.Num(0, 12) +                              // Total Debit Amount
            EftFieldWriter.Num(totalCredits, 12) +                  // Total Credit Amount
            EftFieldWriter.Spaces(39);                               // Reserved
        return EftFieldWriter.Fixed(record, Width);
    }
}
