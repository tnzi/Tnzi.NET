using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 总账明细筛选（关键字 + 来源类型）：SQL 下推、分页与总数口径、筛选后的余额契约、
/// 以及"无筛选时行为不变"的回归红线
/// </summary>
public class GeneralLedgerFilterTests : FinanceIntegrationTestBase
{
    private const string Bank = "1120";
    private const string Income = "4100";

    private static readonly DateTime PeriodFrom = new(2026, 3, 1);
    private static readonly DateTime PeriodTo = new(2026, 3, 31);

    /// <summary>
    /// 捕获 EF 实际执行的 SQL。字段初始化器先于基类构造函数体运行，
    /// 故 <see cref="ConfigureServices"/> 被回调时它已就绪
    /// </summary>
    private readonly SqlCapture _sql = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        // 覆盖基类的 NullLoggerFactory：EF 经应用 ServiceProvider 解析 ILoggerFactory（后注册者胜出），
        // 于是"筛选是否真的下推"能对着真实执行的 SQL 断言，而不是只看返回值
        services.AddSingleton<ILoggerFactory>(_sql);
    }

    // ---------- fixture ----------

    private Task<Guid> BankIdAsync() => AccountIdByCodeAsync(Bank);

    /// <summary>手工过账一笔进 1120（借方 = 收到钱），凭证摘要与行摘要可分别指定</summary>
    private Task<Result<JournalEntryDto>> PostManualAsync(
        string sourceId, decimal amount, DateTime date,
        string? entryMemo = null, string? lineMemo = null, string sourceType = "Test.Manual")
        => PostLedgerAsync(new LedgerPostingRequest
        {
            PostingDate = date,
            Memo = entryMemo,
            SourceType = sourceType,
            SourceId = sourceId,
            Lines =
            [
                new LedgerPostingLine { AccountCode = Bank, Debit = amount, Memo = lineMemo },
                new LedgerPostingLine { AccountCode = Income, Credit = amount }
            ]
        });

    private async Task<Guid> CreateVendorAsync(string name)
    {
        var result = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = name }));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Id;
    }

    private async Task<Guid> CreateCustomerAsync(string name)
    {
        var result = await InScopeAsync<ICustomerService, Result<CustomerDto>>(s => s.CreateAsync(new CreateCustomerDto { Name = name }));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Id;
    }

    private async Task<Guid> PostPaymentAsync(
        FinancePartyType partyType, Guid partyId, decimal amount, DateTime date,
        PaymentDirection direction, string? reference = null)
    {
        var bank = await BankIdAsync();
        var draft = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = direction,
            PartyType = partyType,
            PartyId = partyId,
            DocDate = date,
            Amount = amount,
            DepositToAccountId = bank,
            Reference = reference
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var posted = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        return posted.Data!.Id;
    }

    /// <summary>
    /// 直接写一条支票登记簿记录。BankCheck 无指向 BankAccount 的外键，且本组测试关心的只是
    /// 报表筛选到登记簿的关联（号码 + 状态 + 关联付款单）——真实开票链路由 CheckPrintingTests 覆盖，
    /// 这里刻意不经渲染器，免得筛选测试挂在 PDF 输出上
    /// </summary>
    private async Task SeedCheckAsync(Guid bankAccountId, Guid paymentEntryId, long checkNumber, CheckStatus status)
    {
        var repo = ServiceProvider.GetRequiredService<IRepository<BankCheck, Guid>>();
        await repo.InsertAsync(new BankCheck
        {
            BankAccountId = bankAccountId,
            PaymentEntryId = paymentEntryId,
            CheckNumber = checkNumber,
            Status = status,
            IssueDate = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc)
        });
    }

    /// <summary>
    /// 期内 6 行（顺序即 JE-000002..JE-000007，期初另有 2 月一笔）：
    /// <list type="number">
    /// <item>03-01 凭证摘要 "Downtown office rent"</item>
    /// <item>03-02 行摘要 "Courier charge for filings"</item>
    /// <item>03-03 对照行（凭证摘要 "Bank service fee"，任何关键字都不该命中它）</item>
    /// <item>03-04 付供应商 Acme Supplies，参考号 WIRE-99321，另挂一张 <b>已作废</b> 支票 7799</item>
    /// <item>03-05 付供应商 Globex Trading，参考号 PO-4455，另挂一张 <b>已开具</b> 支票 7788</item>
    /// <item>03-06 收客户 Northwind Traders</item>
    /// </list>
    /// </summary>
    private async Task<Guid> SeedAsync()
    {
        await SeedCoaAsync();
        var bank = await BankIdAsync();

        // 期初（2 月），用于验证无筛选路径的期初余额不为 0
        (await PostManualAsync("m0", 1000m, new DateTime(2026, 2, 1), entryMemo: "Opening float")).Succeeded.ShouldBeTrue();

        (await PostManualAsync("m1", 100m, new DateTime(2026, 3, 1), entryMemo: "Downtown office rent")).Succeeded.ShouldBeTrue();
        (await PostManualAsync("m2", 200m, new DateTime(2026, 3, 2), lineMemo: "Courier charge for filings")).Succeeded.ShouldBeTrue();
        (await PostManualAsync("m3", 300m, new DateTime(2026, 3, 3), entryMemo: "Bank service fee")).Succeeded.ShouldBeTrue();

        var acme = await CreateVendorAsync("Acme Supplies");
        var globex = await CreateVendorAsync("Globex Trading");
        var northwind = await CreateCustomerAsync("Northwind Traders");

        var p1 = await PostPaymentAsync(FinancePartyType.Vendor, acme, 400m, new DateTime(2026, 3, 4), PaymentDirection.Outbound, "WIRE-99321");
        var p2 = await PostPaymentAsync(FinancePartyType.Vendor, globex, 500m, new DateTime(2026, 3, 5), PaymentDirection.Outbound, "PO-4455");
        await PostPaymentAsync(FinancePartyType.Customer, northwind, 600m, new DateTime(2026, 3, 6), PaymentDirection.Inbound);

        var bankAccountId = Guid.NewGuid();
        await SeedCheckAsync(bankAccountId, p2, 7788, CheckStatus.Issued);
        // 对照：作废票的号码仍占位留痕，但按支票号找账只该找到当前有效的票
        await SeedCheckAsync(bankAccountId, p1, 7799, CheckStatus.Void);

        return bank;
    }

    // ---------- helpers ----------

    private Task<Result<GeneralLedgerReportDto>> LedgerAsync(Guid accountId, GeneralLedgerFilterDto? filter, int pageIndex = 1, int pageSize = 50)
        => InScopeAsync<IFinancialReportService, Result<GeneralLedgerReportDto>>(
            s => s.GetGeneralLedgerAsync(accountId, PeriodFrom, PeriodTo, new PagedQueryDto { PageIndex = pageIndex, PageSize = pageSize }, filter));

    private async Task<GeneralLedgerReportDto> ByKeywordAsync(Guid accountId, string keyword)
    {
        var result = await LedgerAsync(accountId, new GeneralLedgerFilterDto { Keyword = keyword });
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!;
    }

    private static List<string?> EntryNumbers(GeneralLedgerReportDto report)
        => [.. report.Lines.Items.Select(l => l.EntryNumber)];

    // ---------- 回归红线：无筛选行为逐字不变 ----------

    [Fact]
    public async Task NoFilter_IsByteForByteTheSameAsBefore()
    {
        var bank = await SeedAsync();
        var paging = new PagedQueryDto { PageIndex = 1, PageSize = 4 };

        // 原 4 参调用点（消费应用的既有写法）与新重载传 null / 传空筛选对象，三者必须完全一致
        var baseline = await InScopeAsync<IFinancialReportService, Result<GeneralLedgerReportDto>>(
            s => s.GetGeneralLedgerAsync(bank, PeriodFrom, PeriodTo, paging));
        var withNull = await LedgerAsync(bank, null, pageSize: 4);
        var withEmpty = await LedgerAsync(bank, new GeneralLedgerFilterDto(), pageSize: 4);

        baseline.Succeeded.ShouldBeTrue(baseline.Message);
        foreach (var report in new[] { baseline.Data!, withNull.Data!, withEmpty.Data! })
        {
            report.IsFiltered.ShouldBeFalse();
            report.OpeningBalance.ShouldBe(1000m);
            report.ClosingBalance.ShouldBe(1000m + 100 + 200 + 300 - 400 - 500 + 600);
            report.Lines.TotalCount.ShouldBe(6);
            report.Lines.Items.Count.ShouldBe(4);
            EntryNumbers(report).ShouldBe(EntryNumbers(baseline.Data!));
            report.Lines.Items.Select(l => l.RunningBalance).ShouldBe(
                baseline.Data!.Lines.Items.Select(l => l.RunningBalance));
        }

        // 运行余额仍从期初逐行累加
        baseline.Data!.Lines.Items.Select(l => l.RunningBalance).ShouldBe([1100m, 1300m, 1600m, 1200m]);
    }

    // ---------- 关键字：五类命中，每类都有必须被排除的对照行 ----------

    [Fact]
    public async Task Keyword_MatchesEntryMemo()
    {
        var bank = await SeedAsync();

        var report = await ByKeywordAsync(bank, "downtown office");

        report.Lines.TotalCount.ShouldBe(1);
        report.Lines.Items.Single().Memo.ShouldBe("Downtown office rent");
        // 对照：同期还有 5 行，其中 "Bank service fee" 摘要相近但不含关键字
        EntryNumbers(report).ShouldNotContain("JE-000004");
    }

    [Fact]
    public async Task Keyword_MatchesLineMemo()
    {
        var bank = await SeedAsync();

        var report = await ByKeywordAsync(bank, "COURIER");

        // 大小写不敏感；行摘要命中，凭证摘要为空的那一行
        report.Lines.TotalCount.ShouldBe(1);
        report.Lines.Items.Single().Memo.ShouldBe("Courier charge for filings");
    }

    [Fact]
    public async Task Keyword_MatchesEntryNumber()
    {
        var bank = await SeedAsync();

        var report = await ByKeywordAsync(bank, "JE-000004");

        report.Lines.TotalCount.ShouldBe(1);
        report.Lines.Items.Single().EntryNumber.ShouldBe("JE-000004");
    }

    [Fact]
    public async Task Keyword_MatchesPaymentReference()
    {
        var bank = await SeedAsync();

        // 参考号只挂在 PaymentEntry 上，凭证与分录行都不带它 —— 命中必须来自 SourceType/SourceId 关联
        var report = await ByKeywordAsync(bank, "wire-99321");

        report.Lines.TotalCount.ShouldBe(1);
        var line = report.Lines.Items.Single();
        line.SourceType.ShouldBe(FinanceSourceTypes.PaymentEntry);
        line.Credit.ShouldBe(400m);

        // 对照：另一张付款单的参考号 PO-4455 不该被 WIRE 关键字带出来
        var other = await ByKeywordAsync(bank, "PO-4455");
        other.Lines.TotalCount.ShouldBe(1);
        other.Lines.Items.Single().Credit.ShouldBe(500m);
    }

    [Fact]
    public async Task Keyword_MatchesIssuedCheckNumber_ButNotVoidedOne()
    {
        var bank = await SeedAsync();

        var issued = await ByKeywordAsync(bank, "7788");

        issued.Lines.TotalCount.ShouldBe(1);
        issued.Lines.Items.Single().Credit.ShouldBe(500m);

        // 对照：作废票的号码占位留痕，但不是"当前有效票据"，按号找账不该找到它
        var voided = await ByKeywordAsync(bank, "7799");
        voided.Lines.TotalCount.ShouldBe(0);
        voided.Lines.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Keyword_MatchesPartyName()
    {
        var bank = await SeedAsync();

        // 往来方名称既不在凭证摘要里（付款凭证摘要是 "Payment made"/"Payment received"）
        // 也不在分录行上，命中必须来自 PaymentEntry.PartyType + PartyId 的解析
        var vendor = await ByKeywordAsync(bank, "acme");
        vendor.Lines.TotalCount.ShouldBe(1);
        vendor.Lines.Items.Single().Credit.ShouldBe(400m);

        var customer = await ByKeywordAsync(bank, "northwind");
        customer.Lines.TotalCount.ShouldBe(1);
        customer.Lines.Items.Single().Debit.ShouldBe(600m);

        // 对照：第三个往来方 Globex 的行不该出现在上面任何一个结果里
        var globex = await ByKeywordAsync(bank, "globex");
        globex.Lines.TotalCount.ShouldBe(1);
        globex.Lines.Items.Single().Credit.ShouldBe(500m);
    }

    [Fact]
    public async Task Keyword_NoMatch_ReturnsEmptyPage()
    {
        var bank = await SeedAsync();

        var report = await ByKeywordAsync(bank, "no-such-thing");

        report.Lines.TotalCount.ShouldBe(0);
        report.Lines.Items.ShouldBeEmpty();
        report.IsFiltered.ShouldBeTrue();
    }

    // ---------- 来源类型 ----------

    [Fact]
    public async Task SourceType_ReturnsOnlyThatType()
    {
        var bank = await SeedAsync();

        var payments = await LedgerAsync(bank, new GeneralLedgerFilterDto { SourceType = FinanceSourceTypes.PaymentEntry });
        payments.Succeeded.ShouldBeTrue(payments.Message);
        payments.Data!.Lines.TotalCount.ShouldBe(3);
        payments.Data.Lines.Items.ShouldAllBe(l => l.SourceType == FinanceSourceTypes.PaymentEntry);

        var manual = await LedgerAsync(bank, new GeneralLedgerFilterDto { SourceType = "Test.Manual" });
        manual.Data!.Lines.TotalCount.ShouldBe(3);
        manual.Data.Lines.Items.ShouldAllBe(l => l.SourceType == "Test.Manual");
    }

    [Fact]
    public async Task KeywordAndSourceType_AreAndedTogether()
    {
        var bank = await SeedAsync();

        // "payment" 命中三张付款凭证的默认摘要；叠加来源类型后仍是三条
        var both = await LedgerAsync(bank, new GeneralLedgerFilterDto
        {
            Keyword = "payment",
            SourceType = FinanceSourceTypes.PaymentEntry
        });
        both.Data!.Lines.TotalCount.ShouldBe(3);

        // 换成手工来源即互斥，一条都不剩 —— 证明两个条件是 AND 而非 OR
        var contradicting = await LedgerAsync(bank, new GeneralLedgerFilterDto
        {
            Keyword = "payment",
            SourceType = "Test.Manual"
        });
        contradicting.Data!.Lines.TotalCount.ShouldBe(0);
    }

    // ---------- ★ 筛选生效时的余额契约 ----------

    [Fact]
    public async Task Filtered_MarksReportAndZeroesEveryBalance()
    {
        var bank = await SeedAsync();

        var report = await ByKeywordAsync(bank, "payment");

        report.IsFiltered.ShouldBeTrue();
        // 筛选后累计余额链条已断，三个余额字段一律置 0 并由 IsFiltered 声明"不适用"，
        // 而不是返回一个逐行累加出来的错数
        report.OpeningBalance.ShouldBe(0m);
        report.ClosingBalance.ShouldBe(0m);
        report.Lines.Items.ShouldAllBe(l => l.RunningBalance == 0m);
        // 行本身的借贷金额仍是真实的
        report.Lines.Items.Sum(l => l.Debit + l.Credit).ShouldBe(1500m);
    }

    // ---------- ★ 筛选下推到 SQL ----------

    [Fact]
    public async Task Filter_IsPushedDownToSql_NotEvaluatedInMemory()
    {
        var bank = await SeedAsync();
        _sql.Clear();

        // 期内共 6 行，其中 3 行命中；页大小 1。若筛选发生在内存（分页之后），
        // TotalCount 会是 6 —— 这一条断言就是"没有静默变成客户端求值"的行为证据
        var page1 = await LedgerAsync(bank, new GeneralLedgerFilterDto { Keyword = "payment" }, pageIndex: 1, pageSize: 1);
        page1.Succeeded.ShouldBeTrue(page1.Message);
        page1.Data!.Lines.TotalCount.ShouldBe(3);
        page1.Data.Lines.Items.Count.ShouldBe(1);

        // 分页游标也走的是筛选后的序列：第 3 页是最后一条命中行，第 4 页为空
        var page3 = await LedgerAsync(bank, new GeneralLedgerFilterDto { Keyword = "payment" }, pageIndex: 3, pageSize: 1);
        page3.Data!.Lines.TotalCount.ShouldBe(3);
        page3.Data.Lines.Items.Count.ShouldBe(1);
        page3.Data.Lines.Items.Single().Debit.ShouldBe(600m);

        var page4 = await LedgerAsync(bank, new GeneralLedgerFilterDto { Keyword = "payment" }, pageIndex: 4, pageSize: 1);
        page4.Data!.Lines.Items.ShouldBeEmpty();

        // 直接对着 EF 实际发出的 SQL 断言：统计总数的那条命令读的是总账行表，
        // 且关键字作为参数进了这条命令 —— 计数在数据库侧就已经是筛选后的口径
        var countCommands = _sql.Commands
            .Where(c => c.Contains("COUNT(*)") && c.Contains("Finance_JournalLine"))
            .ToList();
        countCommands.ShouldNotBeEmpty("总数应由数据库 COUNT 得出");
        countCommands.ShouldContain(c => c.Contains("payment", StringComparison.OrdinalIgnoreCase),
            "COUNT 命令里应带上关键字（EnableSensitiveDataLogging 会打印参数值）");
    }

    [Fact]
    public async Task Filter_JoinsPaymentDomainInSql_NotByFetchingEveryLine()
    {
        var bank = await SeedAsync();
        _sql.Clear();

        var report = await ByKeywordAsync(bank, "acme");
        report.Lines.TotalCount.ShouldBe(1);

        // 读总账行的那几条命令必须自带 WHERE：一旦退化成"先取全量再内存过滤"，
        // 这里就会出现一条无谓词的全表扫描
        var ledgerReads = _sql.Commands.Where(c => c.Contains("Finance_JournalLine")).ToList();
        ledgerReads.ShouldNotBeEmpty();
        ledgerReads.ShouldAllBe(c => c.Contains("WHERE"));
    }

    // ---------- 边界 ----------

    [Fact]
    public async Task BlankFilterValues_AreTreatedAsNoFilter()
    {
        var bank = await SeedAsync();

        var report = await LedgerAsync(bank, new GeneralLedgerFilterDto { Keyword = "   ", SourceType = "" });

        report.Succeeded.ShouldBeTrue(report.Message);
        report.Data!.IsFiltered.ShouldBeFalse();
        report.Data.Lines.TotalCount.ShouldBe(6);
        report.Data.OpeningBalance.ShouldBe(1000m);
    }

    [Fact]
    public async Task Filter_DoesNotBypassAccountOrDateScope()
    {
        var bank = await SeedAsync();
        var income = await AccountIdByCodeAsync(Income);

        // 同一个关键字换个科目：只返回该科目自己的行（付款凭证的对方是 AP，不是收入）
        var onIncome = await ByKeywordAsync(income, "payment");
        onIncome.Lines.TotalCount.ShouldBe(0);

        // 期初那一笔在 2 月，落在期间之外，关键字命中也不该被捞进来
        var opening = await ByKeywordAsync(bank, "opening float");
        opening.Lines.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Filter_AccountNotFound_Returns404()
    {
        await SeedAsync();

        var result = await LedgerAsync(Guid.NewGuid(), new GeneralLedgerFilterDto { Keyword = "payment" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    // ---------- 倒序（descending，网银式最新在最上） ----------

    /// <summary>期初 1000 起，6 行期间的正序运行余额链，供倒序断言逐行对照</summary>
    private static readonly decimal[] AscendingRunning = [1100m, 1300m, 1600m, 1200m, 700m, 1300m];

    [Fact]
    public async Task Descending_FirstPage_ShowsNewestRows_WithClosingBalanceOnTop()
    {
        var bank = await SeedAsync();

        var report = await LedgerAsync(bank, new GeneralLedgerFilterDto { Descending = true }, pageIndex: 1, pageSize: 4);
        report.Succeeded.ShouldBeTrue(report.Message);

        var data = report.Data!;
        data.IsFiltered.ShouldBeFalse();
        data.Lines.TotalCount.ShouldBe(6);          // TotalCount 仍是整个期间的总行数
        data.Lines.Items.Count.ShouldBe(4);

        // 第 1 页 = 期间内最新的 4 行，按 (日期, 凭证号, 行号) 倒序
        EntryNumbers(data).ShouldBe(["JE-000007", "JE-000006", "JE-000005", "JE-000004"]);

        // 最新一行显示 ClosingBalance；每行运行余额仍是“该笔交易后的余额”
        data.Lines.Items[0].RunningBalance.ShouldBe(1300m);
        data.Lines.Items.Select(l => l.RunningBalance).ShouldBe([1300m, 700m, 1200m, 1600m]);

        // 期初/期末与正序完全一致
        data.OpeningBalance.ShouldBe(1000m);
        data.ClosingBalance.ShouldBe(1300m);
    }

    [Fact]
    public async Task Descending_RunningBalanceMatchesAscendingRowForRow()
    {
        var bank = await SeedAsync();

        var asc = await LedgerAsync(bank, new GeneralLedgerFilterDto { Descending = false }, pageSize: 50);
        var desc = await LedgerAsync(bank, new GeneralLedgerFilterDto { Descending = true }, pageSize: 50);
        asc.Succeeded.ShouldBeTrue(asc.Message);
        desc.Succeeded.ShouldBeTrue(desc.Message);

        // 倒序全量 = 正序全量的精确反向：行序反转
        var ascEntriesReversed = EntryNumbers(asc.Data!);
        ascEntriesReversed.Reverse();
        EntryNumbers(desc.Data!).ShouldBe(ascEntriesReversed);

        // 每行运行余额逐字相等（只是显示顺序不同，值不变）
        var ascByEntry = asc.Data!.Lines.Items.ToDictionary(l => l.EntryNumber!, l => l.RunningBalance);
        foreach (var line in desc.Data!.Lines.Items)
            line.RunningBalance.ShouldBe(ascByEntry[line.EntryNumber!]);

        // 正序链本身与预期一致（锚定对照基准）
        asc.Data!.Lines.Items.Select(l => l.RunningBalance).ShouldBe(AscendingRunning);
    }

    [Fact]
    public async Task Descending_LastPage_ReturnsOldestRows_AndBeyondIsEmpty()
    {
        var bank = await SeedAsync();

        // T=6, s=4 → 共 2 页；倒序第 2 页 = 最旧的 2 行
        var page2 = await LedgerAsync(bank, new GeneralLedgerFilterDto { Descending = true }, pageIndex: 2, pageSize: 4);
        page2.Succeeded.ShouldBeTrue(page2.Message);
        page2.Data!.Lines.TotalCount.ShouldBe(6);
        page2.Data.Lines.Items.Count.ShouldBe(2);
        EntryNumbers(page2.Data!).ShouldBe(["JE-000003", "JE-000002"]);
        page2.Data.Lines.Items.Select(l => l.RunningBalance).ShouldBe([1300m, 1100m]);

        // 末页之后：空页，但 TotalCount 仍为整个期间的 6，PageIndex 照回传
        var page3 = await LedgerAsync(bank, new GeneralLedgerFilterDto { Descending = true }, pageIndex: 3, pageSize: 4);
        page3.Data!.Lines.Items.ShouldBeEmpty();
        page3.Data.Lines.TotalCount.ShouldBe(6);
        page3.Data.Lines.PageIndex.ShouldBe(3);
    }

    [Fact]
    public async Task Descending_PageSizeOne_WalksNewestToOldest()
    {
        var bank = await SeedAsync();

        // 页大小 1：逐页从最新走到最旧，每页运行余额是正序链的反向
        var expectedEntries = new[] { "JE-000007", "JE-000006", "JE-000005", "JE-000004", "JE-000003", "JE-000002" };
        var expectedRunning = new[] { 1300m, 700m, 1200m, 1600m, 1300m, 1100m };

        for (var p = 1; p <= 6; p++)
        {
            var page = await LedgerAsync(bank, new GeneralLedgerFilterDto { Descending = true }, pageIndex: p, pageSize: 1);
            page.Succeeded.ShouldBeTrue(page.Message);
            page.Data!.Lines.TotalCount.ShouldBe(6);
            var line = page.Data.Lines.Items.Single();
            line.EntryNumber.ShouldBe(expectedEntries[p - 1]);
            line.RunningBalance.ShouldBe(expectedRunning[p - 1]);
        }
    }

    [Fact]
    public async Task Descending_WithFilter_ReversesRowOrder_ButKeepsBalancesZero()
    {
        var bank = await SeedAsync();

        var asc = await LedgerAsync(bank, new GeneralLedgerFilterDto { Keyword = "payment" });
        var desc = await LedgerAsync(bank, new GeneralLedgerFilterDto { Keyword = "payment", Descending = true });
        asc.Succeeded.ShouldBeTrue(asc.Message);
        desc.Succeeded.ShouldBeTrue(desc.Message);

        // 筛选路径同样套用倒序行序：三张付款凭证由新到旧
        asc.Data!.Lines.TotalCount.ShouldBe(3);
        desc.Data!.Lines.TotalCount.ShouldBe(3);
        var ascEntriesReversed = EntryNumbers(asc.Data!);
        ascEntriesReversed.Reverse();
        EntryNumbers(desc.Data!).ShouldBe(ascEntriesReversed);

        // 余额契约不变：IsFiltered=true、三个余额字段一律置 0
        desc.Data!.IsFiltered.ShouldBeTrue();
        desc.Data.OpeningBalance.ShouldBe(0m);
        desc.Data.ClosingBalance.ShouldBe(0m);
        desc.Data.Lines.Items.ShouldAllBe(l => l.RunningBalance == 0m);
    }

    /// <summary>
    /// 只收 EF 的 Database.Command 日志（含 EnableSensitiveDataLogging 打印的参数值）
    /// </summary>
    private sealed class SqlCapture : ILoggerFactory
    {
        private readonly List<string> _commands = [];

        public IReadOnlyList<string> Commands
        {
            get { lock (_commands) return [.. _commands]; }
        }

        public void Clear()
        {
            lock (_commands) _commands.Clear();
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new CategoryLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class CategoryLogger(SqlCapture owner, string category) : ILogger
        {
            private readonly bool _isCommand = category == DbLoggerCategory.Database.Command.Name;

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => _isCommand;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!_isCommand)
                    return;

                var message = formatter(state, exception);
                lock (owner._commands)
                    owner._commands.Add(message);
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
