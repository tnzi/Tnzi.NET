using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Finance.Documents.Metadata;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 支票版式：从「银行档案 / 请求」到「渲染请求」再到「支票行上的快照」的整条接线
/// </summary>
/// <remarks>
/// ★ 刻意走 <see cref="ICheckService"/> 真实入口并<b>捕获渲染器实际收到的请求</b>，
/// 而不是断言纯函数的返回值：本批改动修的正是「机制建好了没接上」那一类缺陷
/// （<c>CheckLayout</c> 存在、被写进档案、被管理端展示，而默认渲染路径整个忽略它）。
/// 纯函数层的断言在 <c>CheckPrintSettingsTests</c> / <c>CheckLayoutCatalogTests</c>，
/// 它们单独全绿也证明不了这里的任何一条。
/// </remarks>
public class CheckLayoutSnapshotTests : FinanceIntegrationTestBase
{
    private Task<Guid> BankLedgerIdAsync() => AccountIdByCodeAsync("1120");

    [Fact]
    public async Task ThreePerPageLayout_ReachesTheRendererAndIsPinnedOntoTheCheck()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        // 档案不指定模板，只选了「每页三张」——改动之前这个选择在模板驱动路径上完全没有效果
        var bank = await CreateBankAccountAsync(ledger, layout: CheckLayout.ThreePerPage);
        var payment = await CreatePostedCheckPaymentAsync(ledger, await CreateVendorAsync(), 100m);

        var recorder = new RecordingCheckRenderer();
        var print = await PrintAsync(recorder, new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment } });
        print.Succeeded.ShouldBeTrue(print.Message);

        // ① 版式到得了渲染器：解析出的是每页三张那份模板
        recorder.Last!.Layout.ShouldBe(CheckLayout.ThreePerPage);
        recorder.Last.TemplateName.ShouldBe(CheckTemplates.ThreePerPage);

        // ② 并被钉在支票行上（存的是已解析的名字，不是"当时没指定"的 null）
        var issued = await SingleCheckAsync(bank);
        issued.PrintTemplateName.ShouldBe(CheckTemplates.ThreePerPage);
        issued.PrintLayout.ShouldBe(CheckLayout.ThreePerPage);
        issued.PrintStockType.ShouldBe(CheckStockType.PrePrinted);
        issued.PrintOffsetXMm.ShouldBe(0m);
        issued.PrintOffsetYMm.ShouldBe(0m);
    }

    [Fact]
    public async Task RequestTemplate_OverridesTheBankProfileForThatPrintOnly()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(ledger, templateName: CheckTemplates.Cpa006Canada);
        var vendor = await CreateVendorAsync();
        var first = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        var second = await CreatePostedCheckPaymentAsync(ledger, vendor, 250m);

        var recorder = new RecordingCheckRenderer();

        // 这一批用每页三张
        var overridden = await PrintAsync(recorder, new PrintChecksDto
        {
            PaymentEntryIds = new List<Guid> { first },
            TemplateName = CheckTemplates.ThreePerPage
        });
        overridden.Succeeded.ShouldBeTrue(overridden.Message);
        recorder.Last!.TemplateName.ShouldBe(CheckTemplates.ThreePerPage);

        // 下一批不指定 → 回到银行档案，覆盖不粘连
        var plain = await PrintAsync(recorder, new PrintChecksDto { PaymentEntryIds = new List<Guid> { second } });
        plain.Succeeded.ShouldBeTrue(plain.Message);
        recorder.Last!.TemplateName.ShouldBe(CheckTemplates.Cpa006Canada);

        // 两张票各自记住自己那一次用的版式
        var checks = await ChecksAsync(bank);
        checks.Single(c => c.CheckNumber == 1).PrintTemplateName.ShouldBe(CheckTemplates.ThreePerPage);
        checks.Single(c => c.CheckNumber == 2).PrintTemplateName.ShouldBe(CheckTemplates.Cpa006Canada);
    }

    [Fact]
    public async Task RenderingAnIssuedCheck_UsesTheSettingsItWasPrintedWith()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(ledger, templateName: CheckTemplates.Cpa006Canada);
        var payment = await CreatePostedCheckPaymentAsync(ledger, await CreateVendorAsync(), 100m);

        var recorder = new RecordingCheckRenderer();
        (await PrintAsync(recorder, new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment } }))
            .Succeeded.ShouldBeTrue();
        var issued = await SingleCheckAsync(bank);

        // 事后换了票纸厂商：模板改了、偏移也重新校准过
        await MutateBankAsync(bank, b =>
        {
            b.CheckTemplateName = CheckTemplates.VoucherBottomUs;
            b.OffsetXMm = 4m;
            b.OffsetYMm = -3m;
        });

        var render = await RenderAsync(recorder, issued.Id);
        render.Succeeded.ShouldBeTrue(render.Message);

        // 重新渲染的必须还是当初那张纸：模板与偏移都取开票时刻的快照
        recorder.Last!.TemplateName.ShouldBe(CheckTemplates.Cpa006Canada);
        recorder.Last.OffsetXMm.ShouldBe(0m);
        recorder.Last.OffsetYMm.ShouldBe(0m);
    }

    [Fact]
    public async Task RenderingALegacyCheckWithoutASnapshot_StillFollowsTheCurrentBankProfile()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(ledger, templateName: CheckTemplates.Cpa006Canada);
        var payment = await CreatePostedCheckPaymentAsync(ledger, await CreateVendorAsync(), 100m);

        var recorder = new RecordingCheckRenderer();
        (await PrintAsync(recorder, new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment } }))
            .Succeeded.ShouldBeTrue();
        var issued = await SingleCheckAsync(bank);

        // 造出一条存量行：本批改动之前开出的票，五个快照列都是 null
        await MutateCheckAsync(issued.Id, c =>
        {
            c.PrintTemplateName = null;
            c.PrintLayout = null;
            c.PrintStockType = null;
            c.PrintOffsetXMm = null;
            c.PrintOffsetYMm = null;
        });
        await MutateBankAsync(bank, b =>
        {
            b.CheckTemplateName = CheckTemplates.VoucherBottomUs;
            b.OffsetYMm = 2.5m;
        });

        var render = await RenderAsync(recorder, issued.Id);
        render.Succeeded.ShouldBeTrue(render.Message);

        // 无快照 = 跟当前档案，与本机制引入前逐字相同（消费应用只加列、不必回填）
        recorder.Last!.TemplateName.ShouldBe(CheckTemplates.VoucherBottomUs);
        recorder.Last.OffsetYMm.ShouldBe(2.5m);
    }

    [Fact]
    public async Task Reprinting_UsesTheCurrentProfileAndGivesTheReplacementItsOwnSnapshot()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(ledger, templateName: CheckTemplates.Cpa006Canada);
        var payment = await CreatePostedCheckPaymentAsync(ledger, await CreateVendorAsync(), 100m);

        var recorder = new RecordingCheckRenderer();
        (await PrintAsync(recorder, new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment } }))
            .Succeeded.ShouldBeTrue();
        var original = await SingleCheckAsync(bank);

        // 第一张纸打歪了，操作员先调好偏移再重打 —— 重打必须用新的偏移，否则校准永远打不出效果
        await MutateBankAsync(bank, b =>
        {
            b.OffsetXMm = 1.5m;
            b.OffsetYMm = -2m;
        });

        var reprint = await ReprintAsync(recorder, original.Id);
        reprint.Succeeded.ShouldBeTrue(reprint.Message);
        recorder.Last!.OffsetXMm.ShouldBe(1.5m);
        recorder.Last.OffsetYMm.ShouldBe(-2m);

        // 新票是一张新的票据，带着自己这一次的快照 —— 「纸与登记簿一致」对它依然成立
        var replacement = (await ChecksAsync(bank)).Single(c => c.CheckNumber == 2);
        replacement.PrintOffsetXMm.ShouldBe(1.5m);
        replacement.PrintOffsetYMm.ShouldBe(-2m);
        replacement.PrintTemplateName.ShouldBe(CheckTemplates.Cpa006Canada);
    }

    [Fact]
    public async Task TemplateCatalogue_Is501WhenTheRenderingModuleIsNotLoaded()
    {
        // 未加载 Tnzi.Finance.Documents：出厂版式随它分发，目录无从枚举 → 501 引导，
        // 与 print/preview/reprint/calibration 的渲染器缺席处理同构。
        using var scope = ServiceProvider.CreateScope();
        var service = BuildService(scope.ServiceProvider, renderer: null, catalogue: null);

        var result = await service.GetTemplatesAsync();

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(501);
        result.Message.ShouldNotBeNull().ShouldContain("Tnzi.Finance.Documents");
    }

    [Fact]
    public async Task TemplateCatalogue_ListsTheBuiltInLayoutsThroughTheService()
    {
        using var scope = ServiceProvider.CreateScope();
        var service = BuildService(scope.ServiceProvider, new RecordingCheckRenderer(), NewCatalogue());

        var result = await service.GetTemplatesAsync();

        result.Succeeded.ShouldBeTrue(result.Message);
        var names = result.Data.ShouldNotBeNull().Select(t => t.Name).ToList();
        names.ShouldContain(CheckTemplates.ThreePerPage);
        names.ShouldContain(CheckTemplates.VoucherMiddleUs);
    }

    // ── fixture helpers ──────────────────────────────────────

    private static CheckTemplateCatalog NewCatalogue()
        => new(NullLogger<CheckTemplateCatalog>.Instance);

    /// <summary>
    /// 手工装配 <see cref="CheckService"/>：本套用例要观察渲染器<b>收到了什么</b>，
    /// 而基类固定绑定的是出真 PDF 的 PdfSharp 渲染器（沿用同文件既有用例的做法）。
    /// </summary>
    private static CheckService BuildService(IServiceProvider sp, ICheckDocumentRenderer? renderer, ICheckTemplateCatalog? catalogue)
        => new(
            sp,
            sp.GetRequiredService<IRepository<BankCheck, Guid>>(),
            sp.GetRequiredService<IRepository<BankAccount, Guid>>(),
            sp.GetRequiredService<IRepository<PaymentEntry, Guid>>(),
            sp.GetRequiredService<CheckNumberAllocator>(),
            sp.GetRequiredService<IOptionsSnapshot<FinanceOptions>>(),
            sp.GetRequiredService<CheckBatchComposer>(),
            renderer,
            catalogue);

    private async Task<Result<CheckFileDto>> PrintAsync(RecordingCheckRenderer recorder, PrintChecksDto input)
    {
        using var scope = ServiceProvider.CreateScope();
        return await BuildService(scope.ServiceProvider, recorder, NewCatalogue()).PrintAsync(input);
    }

    private async Task<Result<CheckFileDto>> RenderAsync(RecordingCheckRenderer recorder, Guid checkId)
    {
        using var scope = ServiceProvider.CreateScope();
        return await BuildService(scope.ServiceProvider, recorder, NewCatalogue()).RenderAsync(checkId);
    }

    private async Task<Result<CheckFileDto>> ReprintAsync(RecordingCheckRenderer recorder, Guid checkId)
    {
        using var scope = ServiceProvider.CreateScope();
        return await BuildService(scope.ServiceProvider, recorder, NewCatalogue()).ReprintAsync(checkId);
    }

    private async Task<Guid> CreateBankAccountAsync(
        Guid ledgerId,
        CheckLayout layout = CheckLayout.Voucher,
        string? templateName = null)
    {
        var result = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.CreateAsync(new CreateBankAccountDto
        {
            AccountId = ledgerId,
            Name = "Operating",
            Scheme = BankNumberScheme.UsAba,
            RoutingNumber = "021000021",
            AccountNumber = "123456789012",
            NextCheckNumber = 1,
            CheckStockType = CheckStockType.PrePrinted,
            CheckLayout = layout,
            CheckTemplateName = templateName
        }));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Id;
    }

    private async Task<Guid> CreateVendorAsync()
    {
        var result = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = "Acme Supplies" }));
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

    private async Task<List<BankCheck>> ChecksAsync(Guid bankAccountId)
    {
        using var scope = ServiceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<BankCheck, Guid>>();
        return await repository.AsQueryable()
            .Where(c => c.BankAccountId == bankAccountId)
            .OrderBy(c => c.CheckNumber)
            .ToListAsync();
    }

    private async Task<BankCheck> SingleCheckAsync(Guid bankAccountId)
        => (await ChecksAsync(bankAccountId)).ShouldHaveSingleItem();

    private async Task MutateBankAsync(Guid bankAccountId, Action<BankAccount> mutate)
    {
        using var scope = ServiceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<BankAccount, Guid>>();
        var bank = await repository.GetAsync(bankAccountId);
        mutate(bank!);
        await repository.UpdateAsync(bank!);
        await repository.SaveChangesAsync();
    }

    private async Task MutateCheckAsync(Guid checkId, Action<BankCheck> mutate)
    {
        using var scope = ServiceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<BankCheck, Guid>>();
        var check = await repository.GetAsync(checkId);
        mutate(check!);
        await repository.UpdateAsync(check!);
        await repository.SaveChangesAsync();
    }

    /// <summary>记下渲染器实际收到的请求（本套用例断言的对象就是它）。</summary>
    private sealed class RecordingCheckRenderer : ICheckDocumentRenderer
    {
        public CheckRenderRequest? Last { get; private set; }

        public Result<byte[]> Render(CheckRenderRequest request)
        {
            Last = request;
            return Result<byte[]>.Success(new byte[] { 1 });
        }

        public Result<byte[]> RenderCalibration(CheckRenderRequest request)
        {
            Last = request;
            return Result<byte[]>.Success(new byte[] { 1 });
        }
    }
}
