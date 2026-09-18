using System.Text;
using Microsoft.Extensions.Logging;
using Tnzi.Finance.Events;
using Tnzi.Finance.Banking.Events.Handlers;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// P3 块 2：支票打印 / 号码分配 / 生命周期 / 事件联动
/// </summary>
public class CheckPrintingTests : FinanceIntegrationTestBase
{
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
        reloaded.ReplacedByCheckId.ShouldNotBeNull();

        var replacement = await ReloadAsync<BankCheck>(reloaded.ReplacedByCheckId!.Value);
        replacement!.Status.ShouldBe(CheckStatus.Issued);
        replacement.CheckNumber.ShouldBe(2);
        replacement.PaymentEntryId.ShouldBe(p);
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
        (await ReloadAsync<PaymentEntry>(p))!.Reference.ShouldBe("1");

        var original = (await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(
            s => s.GetPagedAsync(new CheckQueryDto { PaymentEntryId = p }))).Data!.Items.Single();
        (await InScopeAsync<ICheckService, Result<CheckFileDto>>(s => s.ReprintAsync(original.Id))).Succeeded.ShouldBeTrue();

        // 旧纸已止付，参考号跟到重打链上仍然有效的那一张
        (await ReloadAsync<PaymentEntry>(p))!.Reference.ShouldBe("2");
    }

    /// <summary>C：手工登记的票同样回写参考号；作废不改写历史事实（Reference 保持原号）。</summary>
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
        (await ReloadAsync<PaymentEntry>(p))!.Reference.ShouldBe("77");

        (await InScopeAsync<ICheckService, Result<BankCheckDto>>(
            s => s.VoidAsync(registered.Data!.Id, new VoidCheckDto { Reason = "Lost" }))).Succeeded.ShouldBeTrue();
        (await ReloadAsync<PaymentEntry>(p))!.Reference.ShouldBe("77");
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
        (await ReloadAsync<PaymentEntry>(payment))!.Reference.ShouldBe("1");
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

    private sealed class FailingCheckRenderer : ICheckDocumentRenderer
    {
        public Result<byte[]> Render(CheckRenderRequest request) => Result<byte[]>.Failure("boom", 500);
        public Result<byte[]> RenderCalibration(CheckRenderRequest request) => Result<byte[]>.Failure("boom", 500);
    }
}
