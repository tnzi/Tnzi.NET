using System.Text;
using Microsoft.EntityFrameworkCore;
using Tnzi.EFCore.Extensions;
using Microsoft.Extensions.Logging;
using Tnzi.Finance.Events;
using Tnzi.Finance.Banking.Events.Handlers;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// P3 块 2：支票打印 / 号码分配 / 生命周期 / 事件联动
/// </summary>
public class CheckPrintingTests : FinanceIntegrationTestBase
{
    private readonly StubLineHook _stubHook = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        // 存根附加行在「已开过票」检查之后、开票事务之前被问一次：竞态用例在那里插入另一个出纳。
        // 平时不挂动作、不给任何附加行，与未注册提供者逐字相同。
        services.AddSingleton(_stubHook);
        services.AddScoped<ICheckStubLineProvider, HookedStubLineProvider>();
    }

    private Task<Guid> BankLedgerIdAsync() => AccountIdByCodeAsync("1120");

    private async Task<Guid> CreateBankAccountAsync(long nextCheckNumber = 1, CheckStockType stock = CheckStockType.PrePrinted)
    {
        var ledger = await BankLedgerIdAsync();
        var result = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.CreateAsync(new CreateBankAccountDto
        {
            AccountId = ledger,
            Name = "Operating",
            Scheme = BankNumberScheme.UsAba,
            RoutingNumber = "021000021",
            AccountNumber = "123456789012",
            NextCheckNumber = nextCheckNumber,
            CheckStockType = stock
        }));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Id;
    }

    private async Task<Guid> CreateVendorAsync(string name = "Acme Supplies")
    {
        var result = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = name }));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Id;
    }

    private async Task<Guid> CreatePostedCheckPaymentAsync(Guid ledgerId, Guid vendorId, decimal amount)
    {
        var draft = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Outbound,
            PartyType = FinancePartyType.Vendor,
            PartyId = vendorId,
            DocDate = new DateTime(2026, 7, 10),
            Amount = amount,
            DepositToAccountId = ledgerId,
            PaymentMethod = PaymentMethods.Check
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var posted = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        return posted.Data!.Id;
    }

    private Task<Result<CheckFileDto>> PrintAsync(PrintChecksDto input)
        => InScopeAsync<ICheckService, Result<CheckFileDto>>(s => s.PrintAsync(input));

    /// <summary>B10：positive-pay 已开票文件 CSV 导出——列出窗口内的支票（号/金额/日期/收款人/签发或作废标志）。</summary>
    [Fact]
    public async Task ExportPositivePay_ListsIssuedChecks()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var p1 = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        (await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { p1 } })).Succeeded.ShouldBeTrue();

        var csv = await InScopeAsync<ICheckService, Result<string>>(
            s => s.ExportPositivePayAsync(bank, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));
        csv.Succeeded.ShouldBeTrue(csv.Message);
        csv.Data!.ShouldContain("CheckNumber,Amount,IssueDate,Payee,Status");
        csv.Data!.ShouldContain("Issued");
        csv.Data!.ShouldContain("Acme Supplies");
    }

    /// <summary>
    /// ★★ 早先签发、本期作废（挂失重打）的票必须出现在本期清单里，标 Void、保留原签发日。
    /// </summary>
    /// <remarks>
    /// 作废不改签发日。只按签发日圈选时，本期清单只有替换票，原票在银行那边仍是有效票，被兑现就是付第二次。
    /// </remarks>
    [Fact]
    public async Task ExportPositivePay_IncludesChecksVoidedInTheWindow_WithTheirOriginalIssueDate()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var payment = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        var issueDate = DateTime.UtcNow.Date.AddDays(-30);
        (await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment }, IssueDate = issueDate })).Succeeded.ShouldBeTrue();
        var original = (await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(
            s => s.GetPagedAsync(new CheckQueryDto { BankAccountId = bank }))).Data!.Items.Single();

        var reprint = await InScopeAsync<ICheckService, Result<CheckFileDto>>(s => s.ReprintAsync(original.Id));
        reprint.Succeeded.ShouldBeTrue(reprint.Message);

        var today = DateTime.UtcNow.Date;
        var csv = await InScopeAsync<ICheckService, Result<string>>(s => s.ExportPositivePayAsync(bank, today, today));
        csv.Succeeded.ShouldBeTrue(csv.Message);
        var rows = csv.Data!.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(r => r.TrimEnd('\r')).ToList();
        rows.ShouldContain(r => r.StartsWith("1,") && r.Contains(issueDate.ToString("yyyy-MM-dd")) && r.EndsWith(",Void"));
        rows.ShouldContain(r => r.StartsWith("2,") && r.EndsWith(",Issued"));

        // 签发那一期的清单照旧列出它（此时已是 Void）。
        var issueWindow = await InScopeAsync<ICheckService, Result<string>>(s => s.ExportPositivePayAsync(bank, issueDate, issueDate));
        var issueRows = issueWindow.Data!.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(r => r.TrimEnd('\r')).ToList();
        issueRows.ShouldHaveSingleItem().ShouldEndWith(",Void");
    }

    /// <summary>对照：作废时刻不在窗口内、签发日也不在窗口内的票不出现。</summary>
    [Fact]
    public async Task ExportPositivePay_LeavesOutChecksIssuedAndVoidedOutsideTheWindow()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var payment = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        var issueDate = DateTime.UtcNow.Date.AddDays(-30);
        (await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment }, IssueDate = issueDate })).Succeeded.ShouldBeTrue();
        var original = (await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(
            s => s.GetPagedAsync(new CheckQueryDto { BankAccountId = bank }))).Data!.Items.Single();
        (await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.VoidAsync(original.Id, new VoidCheckDto { Reason = "Lost" }))).Succeeded.ShouldBeTrue();

        var lastWeek = DateTime.UtcNow.Date.AddDays(-7);
        var csv = await InScopeAsync<ICheckService, Result<string>>(s => s.ExportPositivePayAsync(bank, lastWeek, lastWeek));

        csv.Succeeded.ShouldBeTrue(csv.Message);
        csv.Data!.Trim().ShouldBe("CheckNumber,Amount,IssueDate,Payee,Status");
    }

    /// <summary>回归：Blank 票纸须现打 MICR 行，无 scheme 有效路由时打印须 fail-fast（否则打出空路由字段的
    /// 不可流通票据）。预印票纸不受影响（MICR 已印在票纸上）。</summary>
    [Fact]
    public async Task Print_BlankStock_WithoutRouting_Rejected()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.CreateAsync(new CreateBankAccountDto
        {
            AccountId = ledger,
            Name = "NoRouting",
            Scheme = BankNumberScheme.UsAba,
            RoutingNumber = null,
            AccountNumber = "123456789012",
            NextCheckNumber = 1,
            CheckStockType = CheckStockType.Blank
        }));
        bank.Succeeded.ShouldBeTrue(bank.Message);

        var vendor = await CreateVendorAsync();
        var p = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);

        var print = await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { p } });
        print.Succeeded.ShouldBeFalse();
        print.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Print_AllocatesSequentialNumbers_AndProducesPdf()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var p1 = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        var p2 = await CreatePostedCheckPaymentAsync(ledger, vendor, 250m);

        var queue = await InScopeAsync<ICheckService, Result<List<CheckQueueItemDto>>>(s => s.GetQueueAsync(null));
        queue.Data!.Count.ShouldBe(2);

        var print = await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { p1, p2 } });
        print.Succeeded.ShouldBeTrue(print.Message);
        print.Data!.Content.Length.ShouldBeGreaterThan(100);
        Encoding.ASCII.GetString(print.Data.Content, 0, 5).ShouldBe("%PDF-");

        var checks = await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(s => s.GetPagedAsync(new CheckQueryDto { BankAccountId = bank }));
        checks.Data!.Items.Select(c => c.CheckNumber).OrderBy(n => n).ShouldBe(new long[] { 1, 2 });

        // 打印后队列清空
        var afterQueue = await InScopeAsync<ICheckService, Result<List<CheckQueueItemDto>>>(s => s.GetQueueAsync(null));
        afterQueue.Data!.Count.ShouldBe(0);
    }

    [Fact]
    public async Task RegisterManual_ExplicitCollision_Rejects409()
    {
        await SeedCoaAsync();
        var bank = await CreateBankAccountAsync();

        var first = await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.RegisterManualAsync(new RegisterManualCheckDto
        {
            BankAccountId = bank, CheckNumber = 5, PayeeName = "Cash", Amount = 10m, IssueDate = new DateTime(2026, 7, 1)
        }));
        first.Succeeded.ShouldBeTrue(first.Message);

        var collision = await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.RegisterManualAsync(new RegisterManualCheckDto
        {
            BankAccountId = bank, CheckNumber = 5, PayeeName = "Dup", Amount = 20m, IssueDate = new DateTime(2026, 7, 2)
        }));
        collision.Succeeded.ShouldBeFalse();
        collision.Code.ShouldBe(409);
    }

    [Fact]
    public async Task RegisterManual_AdvancesNextCheckNumber()
    {
        await SeedCoaAsync();
        var bank = await CreateBankAccountAsync();

        var reg = await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.RegisterManualAsync(new RegisterManualCheckDto
        {
            BankAccountId = bank, CheckNumber = 100, PayeeName = "Cash", IssueDate = new DateTime(2026, 7, 1)
        }));
        reg.Succeeded.ShouldBeTrue(reg.Message);

        var entity = await ReloadAsync<BankAccount>(bank);
        entity!.NextCheckNumber.ShouldBe(101);
    }

    [Fact]
    public async Task Spoil_OccupiesNumber_AndAdvances()
    {
        await SeedCoaAsync();
        var bank = await CreateBankAccountAsync();

        var spoil = await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.SpoilAsync(new SpoilCheckDto
        {
            BankAccountId = bank, CheckNumber = 1, Reason = "Jammed"
        }));
        spoil.Succeeded.ShouldBeTrue(spoil.Message);
        spoil.Data!.Status.ShouldBe(CheckStatus.Spoiled);
        (await ReloadAsync<BankCheck>(spoil.Data.Id))!.VoidedTime.ShouldNotBeNull("毁票同样是失效的票，positive-pay 要按失效时刻报给银行");

        var entity = await ReloadAsync<BankAccount>(bank);
        entity!.NextCheckNumber.ShouldBe(2);
    }

    [Fact]
    public async Task Void_MarksVoided()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var p = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { p } });

        var check = (await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(s => s.GetPagedAsync(new CheckQueryDto { BankAccountId = bank }))).Data!.Items[0];
        var voided = await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.VoidAsync(check.Id, new VoidCheckDto { Reason = "Lost" }));
        voided.Succeeded.ShouldBeTrue(voided.Message);
        voided.Data!.Status.ShouldBe(CheckStatus.Void);
        voided.Data.VoidReason.ShouldBe("Lost");
        (await ReloadAsync<BankCheck>(check.Id))!.VoidedTime.ShouldNotBeNull();
    }

    /// <summary>
    /// 队列只排除「已有有效（Issued）支票」的付款：票被作废后，这笔付款回到队列等着重开。
    /// </summary>
    [Fact]
    public async Task Queue_ExcludesIssuedChecksOnly_AVoidedCheckReturnsThePayment()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var printed = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        var waiting = await CreatePostedCheckPaymentAsync(ledger, vendor, 250m);
        await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { printed } });

        var queue = await InScopeAsync<ICheckService, Result<List<CheckQueueItemDto>>>(s => s.GetQueueAsync(null));
        queue.Data!.Select(q => q.PaymentEntryId).ShouldBe(new[] { waiting });
        queue.Data![0].Amount.ShouldBe(250m);
        queue.Data[0].BankAccountId.ShouldBe(bank);

        var check = (await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(s => s.GetPagedAsync(new CheckQueryDto { BankAccountId = bank }))).Data!.Items[0];
        (await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.VoidAsync(check.Id, new VoidCheckDto { Reason = "Lost" }))).Succeeded.ShouldBeTrue();

        var after = await InScopeAsync<ICheckService, Result<List<CheckQueueItemDto>>>(s => s.GetQueueAsync(null));
        after.Data!.Select(q => q.PaymentEntryId).OrderBy(id => id).ShouldBe(new[] { printed, waiting }.OrderBy(id => id));
    }

    // ── 下一张支票号设到已用过的号上 ─────────────────────────────────────────
    //
    // 把下一号设回一个已经开过的号，此后每一次打印都撞唯一索引、回滚又把计数器恢复到同一个号，
    // 于是永远 409「Reload and retry」：重新加载帮不上忙，消息也不指向真正该改的那个设置。

    [Fact]
    public async Task SetNextCheckNumber_ToANumberAlreadyUsed_Returns400_NamingTheNextFreeNumber()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(nextCheckNumber: 1001);
        var vendor = await CreateVendorAsync();
        var p1 = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        var p2 = await CreatePostedCheckPaymentAsync(ledger, vendor, 200m);
        (await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { p1, p2 } })).Succeeded.ShouldBeTrue();

        var set = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(
            s => s.SetNextCheckNumberAsync(bank, new SetNextCheckNumberDto { NextCheckNumber = 1001 }));

        set.Succeeded.ShouldBeFalse();
        set.Code.ShouldBe(400);
        set.Message!.ShouldContain("1001");
        set.Message!.ShouldContain("1003");
        (await ReloadAsync<BankAccount>(bank))!.NextCheckNumber.ShouldBe(1003);
    }

    /// <summary>
    /// 计数器已经指向一个被占用的号（早于上面那道校验写进库的、或数据导入留下的）：
    /// 打印的 409 要指向真正该改的设置，而不是只说「重新加载再试」。
    /// </summary>
    [Fact]
    public async Task Print_WhenTheNextNumberIsAlreadyUsed_Returns409_PointingAtTheNextNumberSetting()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(nextCheckNumber: 7);
        var vendor = await CreateVendorAsync();
        var payment = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        await InScopeAsync<IRepository<BankCheck, Guid>, int>(async repo =>
        {
            await repo.InsertAsync(new BankCheck { BankAccountId = bank, CheckNumber = 7, Status = CheckStatus.Spoiled, IssueDate = new DateTime(2026, 7, 1) });
            await repo.SaveChangesAsync();
            return 0;
        });

        var print = await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment } });

        print.Succeeded.ShouldBeFalse();
        print.Code.ShouldBe(409);
        print.Message!.ShouldContain("next check number");
    }

    /// <summary>对照：换一本号段更低的新支票本是正常操作，只要那个号本身没用过。</summary>
    [Fact]
    public async Task SetNextCheckNumber_BelowExistingChecks_ToAnUnusedNumber_Succeeds()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(nextCheckNumber: 1001);
        var vendor = await CreateVendorAsync();
        var p1 = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        (await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { p1 } })).Succeeded.ShouldBeTrue();

        var set = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(
            s => s.SetNextCheckNumberAsync(bank, new SetNextCheckNumberDto { NextCheckNumber = 500 }));

        set.Succeeded.ShouldBeTrue(set.Message);
        set.Data!.NextCheckNumber.ShouldBe(500);
    }

    /// <summary>
    /// 回写到付款单的参考号与票面、MICR 同一个形状（按 <c>Finance:CheckNumberDigits</c> 补零），
    /// 付款列表里看到的号与纸上、对账单上的号逐字相同。
    /// </summary>
    [Fact]
    public async Task Print_StampsThePaymentReferenceAsPrintedOnTheCheck()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        await CreateBankAccountAsync(nextCheckNumber: 1001);
        var vendor = await CreateVendorAsync();
        var payment = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);

        (await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment } })).Succeeded.ShouldBeTrue();

        (await ReloadAsync<PaymentEntry>(payment))!.Reference.ShouldBe(CheckNumberFormat.Format(1001, CheckNumberFormat.DefaultDigits));
        (await ReloadAsync<PaymentEntry>(payment))!.Reference.ShouldBe("01001");
    }

    [Fact]
    public async Task Reprint_VoidsOriginal_AndChains()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var p = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { p } });

        var original = (await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(s => s.GetPagedAsync(new CheckQueryDto { BankAccountId = bank }))).Data!.Items[0];
        original.CheckNumber.ShouldBe(1);

        var reprint = await InScopeAsync<ICheckService, Result<CheckFileDto>>(s => s.ReprintAsync(original.Id));
        reprint.Succeeded.ShouldBeTrue(reprint.Message);

        var reloaded = await ReloadAsync<BankCheck>(original.Id);
        reloaded!.Status.ShouldBe(CheckStatus.Void);
        reloaded.VoidReason.ShouldBe("Reprinted");
        reloaded.VoidedTime.ShouldNotBeNull();
        reloaded.ReplacedByCheckId.ShouldNotBeNull();

        var replacement = await ReloadAsync<BankCheck>(reloaded.ReplacedByCheckId!.Value);
        replacement!.Status.ShouldBe(CheckStatus.Issued);
        replacement.CheckNumber.ShouldBe(2);
        replacement.PaymentEntryId.ShouldBe(p);
    }

    /// <summary>
    /// 重打分配到的号撞上已有支票：消息要指向下一号设置，而不是「已开过票，请用重打」。
    /// </summary>
    /// <remarks>
    /// 撞号回滚之后，被重打的原票在库里仍是 Issued；按「这笔付款有没有有效票」区分两条唯一索引的话，
    /// 它恒为是，操作员正在重打却被告知去重打，真正该改的计数器设置一个字也没提。
    /// </remarks>
    [Fact]
    public async Task Reprint_WhenTheNextNumberIsAlreadyUsed_Returns409_PointingAtTheNextNumberSetting()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(nextCheckNumber: 1);
        var vendor = await CreateVendorAsync();
        var payment = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        (await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment } })).Succeeded.ShouldBeTrue();
        // 计数器此刻是 2；把 2 号先占掉（数据导入或早于设号校验写进库的毁票）。
        await InScopeAsync<IRepository<BankCheck, Guid>, int>(async repo =>
        {
            await repo.InsertAsync(new BankCheck { BankAccountId = bank, CheckNumber = 2, Status = CheckStatus.Spoiled, IssueDate = new DateTime(2026, 7, 1) });
            await repo.SaveChangesAsync();
            return 0;
        });
        var original = (await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(
            s => s.GetPagedAsync(new CheckQueryDto { BankAccountId = bank }))).Data!.Items.Single(c => c.CheckNumber == 1);

        var reprint = await InScopeAsync<ICheckService, Result<CheckFileDto>>(s => s.ReprintAsync(original.Id));

        reprint.Succeeded.ShouldBeFalse();
        reprint.Code.ShouldBe(409);
        reprint.Message!.ShouldContain("next check number");
        reprint.Message!.ShouldNotContain("Use reprint");
        (await ReloadAsync<BankCheck>(original.Id))!.Status.ShouldBe(CheckStatus.Issued);
    }

    [Fact]
    public async Task PaymentVoidEvent_AutoVoidsIssuedCheck()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var p = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { p } });

        var check = (await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(s => s.GetPagedAsync(new CheckQueryDto { BankAccountId = bank }))).Data!.Items[0];

        using (var scope = ServiceProvider.CreateScope())
        {
            var checkService = scope.ServiceProvider.GetRequiredService<ICheckService>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<PaymentVoidedCheckHandler>>();
            var handler = new PaymentVoidedCheckHandler(logger, checkService);
            await handler.HandleAsync(new FinanceDocumentVoidedEvent { DocType = nameof(PaymentEntry), DocId = p, Number = "PMT-000001" });
        }

        var reloaded = await ReloadAsync<BankCheck>(check.Id);
        reloaded!.Status.ShouldBe(CheckStatus.Void);
        reloaded.VoidedTime.ShouldNotBeNull();
    }

    [Fact]
    public async Task Queue_ExcludesNonCheckPayments()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();

        // BankTransfer 付款不进支票队列
        var transfer = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Outbound, PartyType = FinancePartyType.Vendor, PartyId = vendor,
            DocDate = new DateTime(2026, 7, 10), Amount = 100m, DepositToAccountId = ledger, PaymentMethod = PaymentMethods.BankTransfer
        }));
        await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(transfer.Data!.Id));

        await CreatePostedCheckPaymentAsync(ledger, vendor, 200m);

        var queue = await InScopeAsync<ICheckService, Result<List<CheckQueueItemDto>>>(s => s.GetQueueAsync(null));
        queue.Data!.Count.ShouldBe(1);
        queue.Data[0].Amount.ShouldBe(200m);
    }

    [Fact]
    public async Task Print_RenderFailure_RecoversNumbers()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var p = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);

        using var scope = ServiceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var service = new CheckService(
            sp,
            sp.GetRequiredService<IRepository<BankCheck, Guid>>(),
            sp.GetRequiredService<IRepository<BankAccount, Guid>>(),
            sp.GetRequiredService<IRepository<PaymentEntry, Guid>>(),
            sp.GetRequiredService<CheckNumberAllocator>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<FinanceOptions>>(),
            sp.GetRequiredService<CheckBatchComposer>(),
            // 渲染器现为可选注入，移到构造末位；注入失败渲染器以验证 UoW 回滚回收号
            new FailingCheckRenderer());

        var print = await service.PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { p } });
        print.Succeeded.ShouldBeFalse();

        // 号码回收：NextCheckNumber 未推进，且无支票记录
        var entity = await ReloadAsync<BankAccount>(bank);
        entity!.NextCheckNumber.ShouldBe(1);
        var checks = await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(s => s.GetPagedAsync(new CheckQueryDto { BankAccountId = bank }));
        checks.Data!.Items.Count.ShouldBe(0);
    }

    /// <summary>
    /// ★ 同号重出与重打的票面与首次打印同源：收款人地址（开窗信封）、付款单号、摘要都在，金额大写与票面同币种。
    /// </summary>
    /// <remarks>
    /// 此前两条路径各自手搭一个只有号 / 收款人 / 金额的条目：开窗信封版式重出的那张纸地址栏是空的，
    /// 寄出去没有地址；存根上收款方用来对账的付款单号也没了；重打连摘要都丢了。
    /// 参考号是唯一例外：首次打印后付款单参考号被回写成支票号，那只是重复票号，重出的存根不印它。
    /// </remarks>
    [Fact]
    public async Task RenderAndReprint_CarryTheSameStubFieldsAsTheFirstPrint()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto
        {
            Name = "Acme Supplies", Address = "100 King St W\nToronto ON M5X 1A9"
        }));
        vendor.Succeeded.ShouldBeTrue(vendor.Message);
        var draft = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Outbound, PartyType = FinancePartyType.Vendor, PartyId = vendor.Data!.Id,
            DocDate = new DateTime(2026, 7, 10), Amount = 100m, DepositToAccountId = ledger,
            PaymentMethod = PaymentMethods.Check, Memo = "July rent"
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var posted = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        var payment = posted.Data!.Id;

        using var scope = ServiceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var renderer = new CapturingCheckRenderer(sp.GetRequiredService<ICheckDocumentRenderer>());
        var service = new CheckService(
            sp,
            sp.GetRequiredService<IRepository<BankCheck, Guid>>(),
            sp.GetRequiredService<IRepository<BankAccount, Guid>>(),
            sp.GetRequiredService<IRepository<PaymentEntry, Guid>>(),
            sp.GetRequiredService<CheckNumberAllocator>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<FinanceOptions>>(),
            sp.GetRequiredService<CheckBatchComposer>(),
            renderer);

        (await service.PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment } })).Succeeded.ShouldBeTrue();
        var first = renderer.Items.Single();
        first.PayeeAddressLines.ShouldBe(["100 King St W", "Toronto ON M5X 1A9"]);
        first.PaymentNumber.ShouldNotBeNullOrWhiteSpace();
        first.Memo.ShouldBe("July rent");

        var check = (await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(
            s => s.GetPagedAsync(new CheckQueryDto { BankAccountId = bank }))).Data!.Items.Single();

        renderer.Items.Clear();
        (await service.RenderAsync(check.Id)).Succeeded.ShouldBeTrue();
        var rerendered = renderer.Items.Single();
        rerendered.CheckNumber.ShouldBe(first.CheckNumber);
        rerendered.PayeeName.ShouldBe(first.PayeeName);
        rerendered.PayeeAddressLines.ShouldBe(first.PayeeAddressLines);
        rerendered.PaymentNumber.ShouldBe(first.PaymentNumber);
        rerendered.Memo.ShouldBe(first.Memo);
        rerendered.Amount.ShouldBe(first.Amount);
        rerendered.Currency.ShouldBe(first.Currency);
        rerendered.AmountInWords.ShouldBe(first.AmountInWords);
        rerendered.Reference.ShouldBeNull("付款单参考号此时只是首打回写的票号");

        renderer.Items.Clear();
        (await service.ReprintAsync(check.Id)).Succeeded.ShouldBeTrue();
        var reprinted = renderer.Items.Single();
        reprinted.CheckNumber.ShouldBe(first.CheckNumber + 1);
        reprinted.PayeeName.ShouldBe(first.PayeeName);
        reprinted.PayeeAddressLines.ShouldBe(first.PayeeAddressLines);
        reprinted.PaymentNumber.ShouldBe(first.PaymentNumber);
        reprinted.Memo.ShouldBe("July rent");
        reprinted.Currency.ShouldBe(first.Currency);
        reprinted.AmountInWords.ShouldBe(first.AmountInWords);
        reprinted.Reference.ShouldBeNull();

        // 早先回写的参考号不补零：这样的存量值同样只是票号，不印。
        var current = (await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(
            s => s.GetPagedAsync(new CheckQueryDto { BankAccountId = bank, Status = CheckStatus.Issued }))).Data!.Items.Single();
        await InScopeAsync<IRepository<PaymentEntry, Guid>, int>(async repo =>
        {
            var row = (await repo.GetAsync(payment))!;
            row.Reference = current.CheckNumber.ToString(CultureInfo.InvariantCulture);
            await repo.UpdateAsync(row);
            await repo.SaveChangesAsync();
            return 0;
        });
        renderer.Items.Clear();
        (await service.RenderAsync(current.Id)).Succeeded.ShouldBeTrue();
        renderer.Items.Single().Reference.ShouldBeNull("an unpadded reference stamped before padding is still just the check number");
    }

    [Fact]
    public async Task Print_NoRenderer_Returns501()
    {
        await SeedCoaAsync();

        using var scope = ServiceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        // 渲染器为可选注入：构造时省略末位 renderer（默认 null）= 未加载 Tnzi.Finance.Documents 的场景。
        // 与 ReceiptCaptureTests.Extract_NoExtractor_Returns501 同构：渲染类端点回 501，其余生命周期不受影响。
        var service = new CheckService(
            sp,
            sp.GetRequiredService<IRepository<BankCheck, Guid>>(),
            sp.GetRequiredService<IRepository<BankAccount, Guid>>(),
            sp.GetRequiredService<IRepository<PaymentEntry, Guid>>(),
            sp.GetRequiredService<CheckNumberAllocator>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<FinanceOptions>>(),
            sp.GetRequiredService<CheckBatchComposer>());

        var print = await service.PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { Guid.NewGuid() } });
        print.Succeeded.ShouldBeFalse();
        print.Code.ShouldBe(501);

        var calibration = await service.GetCalibrationPdfAsync(Guid.NewGuid());
        calibration.Succeeded.ShouldBeFalse();
        calibration.Code.ShouldBe(501);
    }

    /// <summary>
    /// A：同号重打（纸没打出来，再打一遍）——零副作用是核心契约：
    /// 号不变、登记簿行数不变、NextCheckNumber 不变、该票 Status/PrintedTime 不变。
    /// </summary>
    [Fact]
    public async Task Render_ReissuesSameNumber_WithZeroSideEffects()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var p = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        (await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { p } })).Succeeded.ShouldBeTrue();

        var issued = (await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(
            s => s.GetPagedAsync(new CheckQueryDto { BankAccountId = bank }))).Data!.Items[0];
        issued.CheckNumber.ShouldBe(1);
        var before = await ReloadAsync<BankCheck>(issued.Id);
        var nextBefore = (await ReloadAsync<BankAccount>(bank))!.NextCheckNumber;

        var render = await InScopeAsync<ICheckService, Result<CheckFileDto>>(s => s.RenderAsync(issued.Id));
        render.Succeeded.ShouldBeTrue(render.Message);
        Encoding.ASCII.GetString(render.Data!.Content, 0, 5).ShouldBe("%PDF-");
        // 同一个号重出：文件名按现有命名 check_{bank.Name}_{CheckNumber}
        render.Data.FileName.ShouldBe("check_Operating_1.pdf");

        // 零副作用：不建新票、不推进号段、不改状态、不动 PrintedTime
        var after = await ReloadAsync<BankCheck>(issued.Id);
        after!.Status.ShouldBe(CheckStatus.Issued);
        after.CheckNumber.ShouldBe(1);
        after.PrintedTime.ShouldBe(before!.PrintedTime);
        after.ReplacedByCheckId.ShouldBeNull();
        (await ReloadAsync<BankAccount>(bank))!.NextCheckNumber.ShouldBe(nextBefore);
        var register = await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(
            s => s.GetPagedAsync(new CheckQueryDto { BankAccountId = bank }));
        register.Data!.Items.Count.ShouldBe(1);
    }

    /// <summary>A：已作废 / 已毁的票不能再出一张可流通的纸 → 409（要么看重打链上的新票，要么走 Reprint）。</summary>
    [Fact]
    public async Task Render_NonIssuedCheck_Rejects409()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var p = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { p } });

        var check = (await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(
            s => s.GetPagedAsync(new CheckQueryDto { BankAccountId = bank }))).Data!.Items[0];
        (await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.VoidAsync(check.Id, new VoidCheckDto { Reason = "Lost" }))).Succeeded.ShouldBeTrue();

        var voidedRender = await InScopeAsync<ICheckService, Result<CheckFileDto>>(s => s.RenderAsync(check.Id));
        voidedRender.Succeeded.ShouldBeFalse();
        voidedRender.Code.ShouldBe(409);

        var spoiled = await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.SpoilAsync(new SpoilCheckDto
        {
            BankAccountId = bank, CheckNumber = 50, Reason = "Jammed"
        }));
        spoiled.Succeeded.ShouldBeTrue(spoiled.Message);

        var spoiledRender = await InScopeAsync<ICheckService, Result<CheckFileDto>>(s => s.RenderAsync(spoiled.Data!.Id));
        spoiledRender.Succeeded.ShouldBeFalse();
        spoiledRender.Code.ShouldBe(409);
    }

    /// <summary>B：按付款单过滤回答"这笔付款是哪张支票付的"——含重打链上的历史票（1 Issued + 1 Void）。</summary>
    [Fact]
    public async Task GetPaged_FilterByPaymentEntry_ReturnsWholeReprintChain()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var p = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        var other = await CreatePostedCheckPaymentAsync(ledger, vendor, 250m);
        await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { p, other } });

        var original = (await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(
            s => s.GetPagedAsync(new CheckQueryDto { PaymentEntryId = p }))).Data!.Items.Single();
        (await InScopeAsync<ICheckService, Result<CheckFileDto>>(s => s.ReprintAsync(original.Id))).Succeeded.ShouldBeTrue();

        var chain = await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(
            s => s.GetPagedAsync(new CheckQueryDto { PaymentEntryId = p }));
        chain.Data!.Items.Count.ShouldBe(2);
        chain.Data.Items.Count(c => c.Status == CheckStatus.Issued).ShouldBe(1);
        chain.Data.Items.Count(c => c.Status == CheckStatus.Void).ShouldBe(1);
        // 另一笔付款的票不混进来
        chain.Data.Items.ShouldAllBe(c => c.PaymentEntryId == p);
        (await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(
            s => s.GetPagedAsync(new CheckQueryDto { BankAccountId = bank }))).Data!.Items.Count.ShouldBe(3);
    }

    /// <summary>C：开票把支票号回写付款单 Reference（框架注释即"外部参考号(支票号/交易号)"）；重打后跟到新号。</summary>
    [Fact]
    public async Task Print_StampsCheckNumberOnPaymentReference()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var p = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        (await ReloadAsync<PaymentEntry>(p))!.Reference.ShouldBeNull();

        (await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { p } })).Succeeded.ShouldBeTrue();
        (await ReloadAsync<PaymentEntry>(p))!.Reference.ShouldBe("00001");

        var original = (await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(
            s => s.GetPagedAsync(new CheckQueryDto { PaymentEntryId = p }))).Data!.Items.Single();
        (await InScopeAsync<ICheckService, Result<CheckFileDto>>(s => s.ReprintAsync(original.Id))).Succeeded.ShouldBeTrue();

        // 旧纸已止付，参考号跟到重打链上仍然有效的那一张
        (await ReloadAsync<PaymentEntry>(p))!.Reference.ShouldBe("00002");
    }

    /// <summary>C：手工登记的票同样回写参考号；作废不改写历史事实（Reference 保持原号）。</summary>
    // ── 手写票挂付款单时的金额 / 币种 / 收款人 ─────────────────────────────────
    //
    // 付款单的这三样是已知的。没填金额的手写票此前以 0.00 进 positive pay（银行据此拒付一张合法的票），
    // 填错的则把一个错的金额预授权给了银行。

    [Fact]
    public async Task RegisterManual_LinkedToAPayment_WithoutAmount_TakesThePaymentsAmountCurrencyAndPayee()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync("Initech LLC");
        var p = await CreatePostedCheckPaymentAsync(ledger, vendor, 345.67m);

        var registered = await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.RegisterManualAsync(new RegisterManualCheckDto
        {
            BankAccountId = bank, CheckNumber = 88, IssueDate = new DateTime(2026, 7, 10), PaymentEntryId = p
        }));

        registered.Succeeded.ShouldBeTrue(registered.Message);
        var check = (await ReloadAsync<BankCheck>(registered.Data!.Id))!;
        check.Amount.ShouldBe(345.67m);
        check.Currency.ShouldBe((await ReloadAsync<PaymentEntry>(p))!.Currency);
        check.PayeeName.ShouldBe("Initech LLC");
    }

    [Fact]
    public async Task RegisterManual_LinkedToAPayment_WithAMismatchingAmount_Rejects400_AndWritesNothing()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var p = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);

        var registered = await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.RegisterManualAsync(new RegisterManualCheckDto
        {
            BankAccountId = bank, CheckNumber = 89, Amount = 1000m, IssueDate = new DateTime(2026, 7, 10), PaymentEntryId = p
        }));

        registered.Succeeded.ShouldBeFalse();
        registered.Code.ShouldBe(400);
        registered.Message!.ShouldContain("amount");
        (await InScopeAsync<IReadOnlyRepository<BankCheck, Guid>, int>(r => r.CountAsync())).ShouldBe(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    public async Task RegisterManual_NonPositiveAmount_Rejects400(int amount)
    {
        await SeedCoaAsync();
        var bank = await CreateBankAccountAsync();

        var registered = await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.RegisterManualAsync(new RegisterManualCheckDto
        {
            BankAccountId = bank, CheckNumber = 90, PayeeName = "Cash", Amount = amount, IssueDate = new DateTime(2026, 7, 10)
        }));

        registered.Succeeded.ShouldBeFalse();
        registered.Code.ShouldBe(400);
    }

    [Fact]
    public async Task RegisterManual_StampsReference_AndVoidLeavesItIntact()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var p = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);

        var registered = await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.RegisterManualAsync(new RegisterManualCheckDto
        {
            BankAccountId = bank, CheckNumber = 77, PayeeName = "Acme Supplies", Amount = 100m,
            IssueDate = new DateTime(2026, 7, 10), PaymentEntryId = p
        }));
        registered.Succeeded.ShouldBeTrue(registered.Message);
        (await ReloadAsync<PaymentEntry>(p))!.Reference.ShouldBe("00077");

        (await InScopeAsync<ICheckService, Result<BankCheckDto>>(
            s => s.VoidAsync(registered.Data!.Id, new VoidCheckDto { Reason = "Lost" }))).Succeeded.ShouldBeTrue();
        (await ReloadAsync<PaymentEntry>(p))!.Reference.ShouldBe("00077");
    }

    /// <summary>
    /// 付款方式的判据必须在写路径上自己成立，不能靠「队列只列 Check 付款」：
    /// <c>PrintAsync</c> 的入参是请求体里的一组 id，从不经过队列。一笔 BankTransfer 付款
    /// （可能已装进 EFT 批次并交给银行）被开成支票 = 同一笔钱付两次，且付款单的参考号
    /// 会被支票号覆盖掉。
    /// </summary>
    [Fact]
    public async Task Print_BankTransferPayment_IsRejected_AndWritesNothing()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var transfer = await CreatePostedPaymentAsync(ledger, vendor, 100m, PaymentMethods.BankTransfer, reference: "WIRE-2026-0042");
        var nextBefore = (await ReloadAsync<BankAccount>(bank))!.NextCheckNumber;

        var print = await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { transfer } });

        print.Succeeded.ShouldBeFalse("a bank-transfer payment must not be printable as a cheque");
        print.Code.ShouldBe(400);
        print.Message!.ShouldContain("not a check payment");

        var checks = ServiceProvider.GetRequiredService<IRepository<BankCheck, Guid>>();
        (await checks.CountAsync(c => c.PaymentEntryId == transfer)).ShouldBe(0);
        (await ReloadAsync<BankAccount>(bank))!.NextCheckNumber.ShouldBe(nextBefore);
        (await ReloadAsync<PaymentEntry>(transfer))!.Reference.ShouldBe("WIRE-2026-0042");
    }

    /// <summary>
    /// 手工登记与打印共用同一个付款方式判据（同一个 <c>finance.check.create</c> 码）：
    /// 请求体里给一笔 BankTransfer 付款的 id，登记簿不能多出一张 Issued 票、
    /// 付款单的电汇参考号也不能被支票号覆盖 —— 否则「两边登记各自看起来完全正常」这条
    /// 打印路径刚关上的口子在登记路径上原样打开。
    /// </summary>
    [Fact]
    public async Task RegisterManual_BankTransferPayment_IsRejected_AndWritesNothing()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(nextCheckNumber: 10);
        var vendor = await CreateVendorAsync();
        var transfer = await CreatePostedPaymentAsync(ledger, vendor, 100m, PaymentMethods.BankTransfer, reference: "WIRE-2026-0042");

        var register = await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.RegisterManualAsync(new RegisterManualCheckDto
        {
            BankAccountId = bank, CheckNumber = 500, PayeeName = "Acme Supplies", Amount = 100m,
            IssueDate = new DateTime(2026, 7, 10), PaymentEntryId = transfer
        }));

        register.Succeeded.ShouldBeFalse("a bank-transfer payment must not be registered as a cheque");
        register.Code.ShouldBe(400);
        register.Message!.ShouldContain("not a check payment");

        var checks = ServiceProvider.GetRequiredService<IRepository<BankCheck, Guid>>();
        (await checks.CountAsync(c => c.BankAccountId == bank)).ShouldBe(0);
        (await ReloadAsync<BankAccount>(bank))!.NextCheckNumber.ShouldBe(10);
        (await ReloadAsync<PaymentEntry>(transfer))!.Reference.ShouldBe("WIRE-2026-0042");
    }

    /// <summary>不存在的付款单 id 不能静默登记成一张「付了某笔款」的票（回写会 no-op，登记簿却多了一行）。</summary>
    [Fact]
    public async Task RegisterManual_UnknownPayment_Rejects404_AndWritesNothing()
    {
        await SeedCoaAsync();
        var bank = await CreateBankAccountAsync();

        var register = await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.RegisterManualAsync(new RegisterManualCheckDto
        {
            BankAccountId = bank, CheckNumber = 500, PayeeName = "Nobody", Amount = 1m,
            IssueDate = new DateTime(2026, 7, 10), PaymentEntryId = Guid.NewGuid()
        }));

        register.Succeeded.ShouldBeFalse();
        register.Code.ShouldBe(404);
        var checks = ServiceProvider.GetRequiredService<IRepository<BankCheck, Guid>>();
        (await checks.CountAsync(c => c.BankAccountId == bank)).ShouldBe(0);
    }

    /// <summary>付款单的出款科目必须就是所选银行档案挂的科目：一张票不能登记在另一家银行的登记簿上。</summary>
    [Fact]
    public async Task RegisterManual_PaymentFundedFromAnotherBankAccount_Rejects400()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        await CreateBankAccountAsync();
        var otherLedger = await AccountIdByCodeAsync("1110");
        var otherBank = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.CreateAsync(new CreateBankAccountDto
        {
            AccountId = otherLedger, Name = "Payroll", Scheme = BankNumberScheme.UsAba,
            RoutingNumber = "021000021", AccountNumber = "999999999999", NextCheckNumber = 1
        }));
        otherBank.Succeeded.ShouldBeTrue(otherBank.Message);
        var vendor = await CreateVendorAsync();
        var payment = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);

        var register = await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.RegisterManualAsync(new RegisterManualCheckDto
        {
            BankAccountId = otherBank.Data!.Id, CheckNumber = 500, PayeeName = "Acme Supplies", Amount = 100m,
            IssueDate = new DateTime(2026, 7, 10), PaymentEntryId = payment
        }));

        register.Succeeded.ShouldBeFalse();
        register.Code.ShouldBe(400);
        (await ReloadAsync<PaymentEntry>(payment))!.Reference.ShouldBeNull();
    }

    /// <summary>已有 Issued 票的付款单不能再登记第二张（与打印路径同一条 409；换票走 Reprint）。</summary>
    [Fact]
    public async Task RegisterManual_PaymentAlreadyIssued_Rejects409_AndKeepsReference()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var payment = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        (await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment } })).Succeeded.ShouldBeTrue();

        var register = await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.RegisterManualAsync(new RegisterManualCheckDto
        {
            BankAccountId = bank, CheckNumber = 500, PayeeName = "Acme Supplies", Amount = 100m,
            IssueDate = new DateTime(2026, 7, 10), PaymentEntryId = payment
        }));

        register.Succeeded.ShouldBeFalse();
        register.Code.ShouldBe(409);
        (await ReloadAsync<PaymentEntry>(payment))!.Reference.ShouldBe("00001");
    }

    /// <summary>预览与打印共用同一条解析路径，收紧一处两边都收紧 —— 这条锁住共用没有被拆开。</summary>
    [Fact]
    public async Task Preview_BankTransferPayment_IsRejected()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var transfer = await CreatePostedPaymentAsync(ledger, vendor, 100m, PaymentMethods.BankTransfer);

        var preview = await InScopeAsync<ICheckService, Result<CheckFileDto>>(
            s => s.PreviewAsync(new PreviewChecksDto { PaymentEntryIds = new List<Guid> { transfer } }));

        preview.Succeeded.ShouldBeFalse();
        preview.Code.ShouldBe(400);
    }

    /// <summary>付款方式比较不区分大小写（队列路径比的是小写，写路径要与之一致，否则 "check" 进得了队列却打不了）。</summary>
    [Fact]
    public async Task Print_AcceptsCheckMethod_RegardlessOfCase()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var lower = await CreatePostedPaymentAsync(ledger, vendor, 100m, "check");

        var print = await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { lower } });

        print.Succeeded.ShouldBeTrue(print.Message);
    }

    /// <summary>
    /// ★★ 两个出纳同时打印同一笔付款（或双击、超时重试）：只能开出一张有效支票。
    /// </summary>
    /// <remarks>
    /// 「已开过票」检查在事务外读，两个请求会各自通过、各分配一个号，登记簿上留下两张可流通的票，
    /// positive-pay 两张都报，收款方两张都能兑。第二个打印插在第一个的检查与开票事务之间。
    /// </remarks>
    [Fact]
    public async Task Print_ConcurrentPrintOfTheSamePayment_SecondIsRejected_AndOnlyOneCheckIsIssued()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var payment = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        Result<CheckFileDto>? first = null;
        _stubHook.Next = async () => first = await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment } });

        var second = await PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment } });

        first.ShouldNotBeNull("钩子必须真的插进了打印");
        first.Succeeded.ShouldBeTrue(first.Message);
        second.Succeeded.ShouldBeFalse("同一笔付款已经开出一张有效支票");
        second.Code.ShouldBe(409);
        second.Message!.ShouldContain("already have an issued check");
        var checks = await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(
            s => s.GetPagedAsync(new CheckQueryDto { BankAccountId = bank }));
        checks.Data!.Items.Count(c => c.Status == CheckStatus.Issued && c.PaymentEntryId == payment).ShouldBe(1);
        // 被拒的那次连号码都不占：它在建票之前就回滚了。
        (await ReloadAsync<BankAccount>(bank))!.NextCheckNumber.ShouldBe(2);
    }

    /// <summary>
    /// 兜底：不经号码行锁的写入（手工登记的并发窗口）同样写不进第二张 Issued 票；作废 / 毁票的行不受约束。
    /// </summary>
    [Fact]
    public async Task Register_HoldsAtMostOneIssuedCheckPerPayment()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorAsync();
        var payment = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);

        async Task InsertAsync(long number, CheckStatus status, Guid? paymentId)
        {
            using var scope = ServiceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<BankCheck, Guid>>();
            await repo.InsertAsync(new BankCheck
            {
                BankAccountId = bank, CheckNumber = number, Status = status, PaymentEntryId = paymentId,
                IssueDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc)
            });
            await repo.SaveChangesAsync();
        }

        await InsertAsync(10, CheckStatus.Void, payment);
        await InsertAsync(11, CheckStatus.Issued, payment);
        await InsertAsync(12, CheckStatus.Spoiled, null);
        await InsertAsync(13, CheckStatus.Spoiled, null);

        var duplicate = await Should.ThrowAsync<DbUpdateException>(() => InsertAsync(14, CheckStatus.Issued, payment));
        duplicate.IsUniqueConstraintViolation().ShouldBeTrue();
    }

    private async Task<Guid> CreatePostedPaymentAsync(Guid ledgerId, Guid vendorId, decimal amount, string method, string? reference = null)
    {
        var draft = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Outbound,
            PartyType = FinancePartyType.Vendor,
            PartyId = vendorId,
            DocDate = new DateTime(2026, 7, 10),
            Amount = amount,
            DepositToAccountId = ledgerId,
            PaymentMethod = method,
            Reference = reference
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var posted = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        return posted.Data!.Id;
    }

    /// <summary>竞态插入点：只触发一次，触发前先清空（插入的那个操作自己不会再触发它）。</summary>
    private sealed class StubLineHook
    {
        public Func<Task>? Next { get; set; }
    }

    private sealed class HookedStubLineProvider(StubLineHook hook) : ICheckStubLineProvider
    {
        public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<CheckStubLine>>> GetStubLinesAsync(
            IReadOnlyList<Guid> paymentEntryIds, CancellationToken cancellationToken = default)
        {
            var next = hook.Next;
            hook.Next = null;
            if (next != null)
                await next();
            return new Dictionary<Guid, IReadOnlyList<CheckStubLine>>();
        }
    }

    /// <summary>记下每次渲染请求里的票面条目，再交给真实渲染器出字节。</summary>
    private sealed class CapturingCheckRenderer(ICheckDocumentRenderer inner) : ICheckDocumentRenderer
    {
        private readonly ICheckDocumentRenderer _inner = inner;

        public List<CheckRenderItem> Items { get; } = new();

        public Result<byte[]> Render(CheckRenderRequest request)
        {
            Items.AddRange(request.Checks);
            return _inner.Render(request);
        }

        public Result<byte[]> RenderCalibration(CheckRenderRequest request) => _inner.RenderCalibration(request);
    }

    private sealed class FailingCheckRenderer : ICheckDocumentRenderer
    {
        public Result<byte[]> Render(CheckRenderRequest request) => Result<byte[]>.Failure("boom", 500);
        public Result<byte[]> RenderCalibration(CheckRenderRequest request) => Result<byte[]>.Failure("boom", 500);
    }
}
