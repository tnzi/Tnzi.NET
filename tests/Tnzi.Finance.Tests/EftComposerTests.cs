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

    // ── 账号超长：截断出的是另一个人的账号 ─────────────────────────────────────

    /// <summary>
    /// 收款方账号超过 NACHA 的 17 位账号字段 → 拒绝组装，绝不截断。
    /// </summary>
    /// <remarks>
    /// 截断一个自由文本字段只是难看；截断一个账号得到的是<b>另一个语法合法的账号</b>，
    /// 银行按它处理，钱要么退回要么进了别人的户头 —— 而录入、装批、生成三步全程 200。
    /// 出款方 <c>OriginatorId</c> 超 9 位一直是 fail-fast 的，收款方这侧没有理由更宽松。
    /// </remarks>
    [Fact]
    public void Nacha_PayeeAccountNumberLongerThanTheField_IsRejected()
    {
        var request = NachaRequest();
        request.Entries[0].AccountNumber = new string('7', 18); // 字段宽 17

        var result = new DefaultEftFileComposer().Compose(request);

        result.Succeeded.ShouldBeFalse("超长账号必须被拒绝而不是截断成另一个账号");
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("JOHN DOE"); // 指名是谁的账号，操作员才知道去改哪一条
        result.Message!.ShouldNotContain("777777"); // 账号本身不进错误消息
    }

    /// <summary>恰好等于字段宽度的账号照常写入 —— 上限是 &gt; 不是 &gt;=。</summary>
    [Fact]
    public void Nacha_PayeeAccountNumberExactlyTheFieldWidth_IsAccepted()
    {
        var request = NachaRequest();
        request.Entries[0].AccountNumber = new string('7', 17);

        var result = new DefaultEftFileComposer().Compose(request);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Content.ShouldContain(new string('7', 17));
    }

    /// <summary>CPA-005 的收款方账号字段只有 12 位，同样不得截断。</summary>
    [Fact]
    public void Cpa005_PayeeAccountNumberLongerThanTheField_IsRejected()
    {
        var request = Cpa005Request();
        request.Entries[1].AccountNumber = new string('3', 13); // 字段宽 12

        var result = new DefaultEftFileComposer().Compose(request);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("JANE ROE");
    }

    /// <summary>
    /// 出款方账号同样超不得 —— CPA-005 的 Originator Account Number 也只有 12 位。
    /// </summary>
    /// <remarks>
    /// 这一侧被截断的后果不是一笔付款出错，而是<b>整份报文从一个不存在的户头扣款</b>。
    /// </remarks>
    [Fact]
    public void Cpa005_OriginatorAccountNumberLongerThanTheField_IsRejected()
    {
        var request = Cpa005Request();
        request.OriginatorAccountNumber = new string('1', 13);

        var result = new DefaultEftFileComposer().Compose(request);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("originating account");
    }

    /// <summary>
    /// 负额条目 → 拒绝组装，而不是静默翻正。
    /// </summary>
    /// <remarks>
    /// 两种格式在本实现里都只出 credit，定宽金额字段里没有符号位。翻正之后
    /// 账上记的是收回一笔钱、银行收到的是付出同一笔钱 —— 金额一模一样、方向相反，
    /// 对账反而对得上。核心的付款路径当前不可能产出负额付款，这一条是兜底。
    /// </remarks>
    [Fact]
    public void Nacha_NegativeAmount_IsRejected()
    {
        var request = NachaRequest();
        request.Entries[0].Amount = -100m;

        var result = new DefaultEftFileComposer().Compose(request);

        result.Succeeded.ShouldBeFalse("负额不得被翻正成一笔付出去的钱");
        result.Code.ShouldBe(400);
    }

    /// <summary>CPA-005 侧同理（同一个金额写入器）。</summary>
    [Fact]
    public void Cpa005_NegativeAmount_IsRejected()
    {
        var request = Cpa005Request();
        request.Entries[1].Amount = -250.50m;

        var result = new DefaultEftFileComposer().Compose(request);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>
    /// 金额装不进定宽字段 → 拒绝，绝不丢高位。
    /// </summary>
    /// <remarks>
    /// ★ 自查时按「账号被静默截断」同一形态回看金额路径找到的：定宽数值写入器超长时保留的是
    /// <b>低位</b>（对循环序号、trace 号是对的），而对金额意味着一笔 100,000,000.00 的付款
    /// 在 10 位字段里变成 <b>0.00</b> —— 数字合法、文件定长、每一步 200，只是少了一个数量级。
    /// NACHA 明细金额 10 位（上限 99,999,999.99）是格式规定，不是本框架的选择。
    /// </remarks>
    [Fact]
    public void Nacha_AmountWiderThanTheField_IsRejected()
    {
        var request = NachaRequest();
        request.Entries[0].Amount = 100_000_000.00m; // 10_000_000_000 分 = 11 位

        var result = new DefaultEftFileComposer().Compose(request);

        result.Succeeded.ShouldBeFalse("装不下的金额必须被拒绝而不是丢掉高位");
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("JOHN DOE");
    }

    /// <summary>恰好用满字段的金额照常写入（上限是 &gt; 不是 &gt;=）。</summary>
    [Fact]
    public void Nacha_AmountExactlyFillingTheField_IsAccepted()
    {
        var request = NachaRequest();
        request.Entries[0].Amount = 99_999_999.99m; // 9_999_999_999 分 = 10 位

        var result = new DefaultEftFileComposer().Compose(request);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Content.ShouldContain("9999999999");
    }

    /// <summary>合计字段同理：单笔都装得下，加起来装不下也必须拒绝。</summary>
    [Fact]
    public void Nacha_BatchTotalWiderThanTheField_IsRejected()
    {
        var request = NachaRequest();
        // 合计字段 12 位（上限 9,999,999,999.99）；两笔各 99,999,999.99 的明细装得下，
        // 这里直接造一批把合计推过去。
        request.Entries.Clear();
        for (var i = 0; i < 101; i++)
        {
            request.Entries.Add(new EftComposeEntry
            {
                PayeeName = $"PAYEE {i}", RoutingNumber = "011401533", AccountNumber = "1234567",
                AccountType = BankAccountType.Checking, Amount = 99_999_999.99m
            });
        }

        var result = new DefaultEftFileComposer().Compose(request);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("total");
    }

    /// <summary>CPA-005 侧同理（明细字段同为 10 位）。</summary>
    [Fact]
    public void Cpa005_AmountWiderThanTheField_IsRejected()
    {
        var request = Cpa005Request();
        request.Entries[1].Amount = 100_000_000.00m;

        var result = new DefaultEftFileComposer().Compose(request);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("JANE ROE");
    }

    // ── 路由缺失：九个零是一份语法合法、指向不存在机构的报文 ──────────────────

    /// <summary>
    /// 出款方机构号/分行号缺失 → 拒绝组装，而不是把 originating transit 写成九个零。
    /// </summary>
    /// <remarks>
    /// 档案本身允许留空路由（只登记名称与账号是合法的），所以这个判据不能挪到录入校验上；
    /// 它属于「要出文件了」那一刻。NACHA 侧对 ODFI 路由一直是 fail-fast 的，
    /// CPA-005 侧此前一路放行，整份文件的出款路由变成 000000000。
    /// </remarks>
    [Fact]
    public void Cpa005_OriginatorRoutingMissing_IsRejected()
    {
        var request = Cpa005Request();
        request.OriginatorInstitutionNumber = null;
        request.OriginatorTransitNumber = null;

        var result = new DefaultEftFileComposer().Compose(request);

        result.Succeeded.ShouldBeFalse("出款方路由缺失不得被补成九个零");
        result.Code.ShouldBe(400);
    }

    /// <summary>分行号缺一半也不行 —— 补零补出来的是另一家分行。</summary>
    [Fact]
    public void Cpa005_OriginatorTransitMissing_IsRejected()
    {
        var request = Cpa005Request();
        request.OriginatorTransitNumber = null;

        var result = new DefaultEftFileComposer().Compose(request);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>收款方路由缺失同样拒绝，并指名是哪一位收款人。</summary>
    [Fact]
    public void Cpa005_PayeeRoutingMissing_IsRejected()
    {
        var request = Cpa005Request();
        request.Entries[1].InstitutionNumber = null;

        var result = new DefaultEftFileComposer().Compose(request);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("JANE ROE");
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
