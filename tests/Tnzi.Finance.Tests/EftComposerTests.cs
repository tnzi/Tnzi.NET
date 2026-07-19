namespace Tnzi.Finance.Tests;

/// <summary>
/// P3 块 3：NACHA / CPA-005 文件组装（纯函数，黄金文件断言）
/// </summary>
public class EftComposerTests
{
    private static EftComposeRequest NachaRequest(int fileCreationNumber = 1) => new()
    {
        Format = EftFileFormat.Nacha,
        Currency = "USD",
        EffectiveDate = new DateTime(2026, 7, 20),
        CreationTime = new DateTime(2026, 7, 13, 10, 30, 0),
        FileCreationNumber = fileCreationNumber,
        OriginatorId = "123456789",
        OriginatorName = "ACME CORP",
        BankName = "FIRST BANK",
        OriginatorRoutingNumber = "021000021",
        OriginatorAccountNumber = "111222333",
        Entries =
        {
            new EftComposeEntry { PayeeName = "JOHN DOE", RoutingNumber = "011401533", AccountNumber = "1234567", AccountType = BankAccountType.Checking, Amount = 100.00m },
            new EftComposeEntry { PayeeName = "JANE ROE", RoutingNumber = "121000358", AccountNumber = "7654321", AccountType = BankAccountType.Savings, Amount = 250.50m }
        }
    };

    private static EftComposeRequest Cpa005Request(int fileCreationNumber = 1) => new()
    {
        Format = EftFileFormat.Cpa005,
        Currency = "CAD",
        EffectiveDate = new DateTime(2026, 7, 20),
        CreationTime = new DateTime(2026, 7, 13, 10, 30, 0),
        FileCreationNumber = fileCreationNumber,
        OriginatorId = "CPA0012345",
        OriginatorName = "ACME CORP",
        OriginatorInstitutionNumber = "001",
        OriginatorTransitNumber = "12345",
        OriginatorAccountNumber = "111222333",
        Entries =
        {
            new EftComposeEntry { PayeeName = "JOHN DOE", InstitutionNumber = "002", TransitNumber = "54321", AccountNumber = "1234567", AccountType = BankAccountType.Checking, Amount = 100.00m },
            new EftComposeEntry { PayeeName = "JANE ROE", InstitutionNumber = "003", TransitNumber = "67890", AccountNumber = "7654321", AccountType = BankAccountType.Savings, Amount = 250.50m }
        }
    };

    [Fact]
    public void Nacha_FixedWidth_And_Totals()
    {
        var result = new DefaultEftFileComposer().Compose(NachaRequest());
        result.Succeeded.ShouldBeTrue(result.Message);

        var lines = result.Data!.Content.Split('\n');
        lines.All(l => l.Length == 94).ShouldBeTrue("every NACHA record must be 94 characters");
        (lines.Length % 10).ShouldBe(0); // 块填充到 10 的整数倍
        lines.Length.ShouldBe(10);

        lines[0][0].ShouldBe('1'); // File Header
        lines[1][0].ShouldBe('5'); // Batch Header
        lines[2][0].ShouldBe('6'); // Entry Detail
        lines[3][0].ShouldBe('6');
        lines[4][0].ShouldBe('8'); // Batch Control
        lines[5][0].ShouldBe('9'); // File Control
        lines[6].ShouldBe(new string('9', 94)); // padding

        // 总贷方金额（分）= 100.00 + 250.50 = 350.50 → 000000035050（12 位）
        result.Data.Content.ShouldContain("000000035050");
        // File Control 条目数 8 位
        lines[5].ShouldContain("00000002");
    }

    /// <summary>回归：自由文本字段（PayeeName 等）内嵌的换行/制表符必须被剥除，
    /// 否则一条 94 字节 NACHA 记录会被 \n 截成两行、错位后续所有字段 → 整文件被 ODFI 拒收。</summary>
    [Fact]
    public void Nacha_PayeeNameWithControlChars_DoesNotSplitRecords()
    {
        var request = NachaRequest();
        request.Entries[0].PayeeName = "ACME\nINC\tCO";
        var result = new DefaultEftFileComposer().Compose(request);
        result.Succeeded.ShouldBeTrue(result.Message);

        var lines = result.Data!.Content.Split('\n');
        lines.All(l => l.Length == 94).ShouldBeTrue("控制字符不得把一条定宽记录截成两行");
        lines.Length.ShouldBe(10); // 与干净名一致，未被换行撑出额外行
    }

    [Fact]
    public void Cpa005_FixedWidth_And_Totals()
    {
        var result = new DefaultEftFileComposer().Compose(Cpa005Request());
        result.Succeeded.ShouldBeTrue(result.Message);

        var lines = result.Data!.Content.Split('\n');
        lines.All(l => l.Length == 1464).ShouldBeTrue("every CPA-005 record must be 1464 characters");
        lines.Length.ShouldBe(4); // A + 2×C + Z

        lines[0][0].ShouldBe('A');
        lines[1][0].ShouldBe('C');
        lines[2][0].ShouldBe('C');
        lines[3][0].ShouldBe('Z');

        // Payee Institution/Transit（C 段偏移 19 → 记录位置 43，9 位）：电子路由 = "0" + 机构(3) + 分行(5)
        // JOHN DOE 机构 002/分行 54321 → "000254321"；JANE ROE 机构 003/分行 67890 → "000367890"
        lines[1].Substring(43, 9).ShouldBe("000254321");
        lines[2].Substring(43, 9).ShouldBe("000367890");
        // Originating Institution/Transit（C 段偏移 140 → 记录位置 164）：机构 001/分行 12345 → "000112345"
        lines[1].Substring(164, 9).ShouldBe("000112345");

        // Z 记录：总贷方金额（分，14 位）+ 笔数（8 位）
        lines[3].ShouldContain("00000000035050");
        lines[3].ShouldContain("00000002");
    }

    [Fact]
    public void Cpa005_FileCreationNumber_Increments()
    {
        var first = new DefaultEftFileComposer().Compose(Cpa005Request(1));
        var second = new DefaultEftFileComposer().Compose(Cpa005Request(2));

        // A 记录 file creation number 字段位于位置 20-23（"A" + 9 位记录数 + 10 位 originator id）
        first.Data!.Content.Split('\n')[0].Substring(20, 4).ShouldBe("0001");
        second.Data!.Content.Split('\n')[0].Substring(20, 4).ShouldBe("0002");
    }
}
