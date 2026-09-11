using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Finance.Documents.Metadata;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 版式样张：零业务数据渲染一张占位支票
/// </summary>
/// <remarks>
/// ★ 走 <see cref="ICheckService"/> 真实入口并<b>捕获渲染器实际收到的请求</b>。
/// 最要紧的两条不是"能出图"，而是：
/// ①<b>零副作用</b>——不分配号、不写登记簿，不需要任何付款单 / 收款人 / 应付单；
/// ②<b>永不带出真账号</b>——样张会被下载被打印，白纸票纸下磁码行是真的印出来的，
///   水印挡人眼挡不住读票机。
/// </remarks>
public class CheckSpecimenTests : FinanceIntegrationTestBase
{
    private Task<Guid> BankLedgerIdAsync() => AccountIdByCodeAsync("1120");

    [Fact]
    public async Task Specimen_RendersWithNoBusinessDataAtAll()
    {
        // 刻意不建银行账户、不建供应商、不建付款单——这正是本功能存在的理由
        await SeedCoaAsync();

        var recorder = new RecordingCheckRenderer();
        var result = await SpecimenAsync(recorder, CheckTemplates.Cpa006Canada);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.FileName.ShouldBe($"specimen_{CheckTemplates.Cpa006Canada}.html");

        var request = recorder.Last.ShouldNotBeNull();
        request.TemplateName.ShouldBe(CheckTemplates.Cpa006Canada);
        request.Checks.ShouldHaveSingleItem().PayeeName.ShouldNotBeNullOrWhiteSpace();
        // 每个位置都有东西，否则用户看不出那一处会落在纸上的哪里
        var item = request.Checks[0];
        item.Amount.ShouldBeGreaterThan(0m);
        item.AmountInWords.ShouldNotBeNullOrWhiteSpace();
        item.PayeeAddressLines.ShouldNotBeEmpty();
        item.StubLines.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Specimen_LeavesTheRegisterAndTheNumberSequenceUntouched()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(ledger);
        var nextBefore = (await ReloadAsync<BankAccount>(bank))!.NextCheckNumber;

        var recorder = new RecordingCheckRenderer();
        (await SpecimenAsync(recorder, CheckTemplates.Cpa006Canada, bankAccountId: bank)).Succeeded.ShouldBeTrue();

        // 零副作用：号段没动，登记簿没有新行
        (await ReloadAsync<BankAccount>(bank))!.NextCheckNumber.ShouldBe(nextBefore);
        var register = await InScopeAsync<ICheckService, Result<IPagedList<BankCheckDto>>>(
            s => s.GetPagedAsync(new CheckQueryDto { BankAccountId = bank }));
        register.Data!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ThreeUpLayout_DrawsThreeChequesSoTheStockIsRecognisable()
    {
        await SeedCoaAsync();

        var recorder = new RecordingCheckRenderer();
        (await SpecimenAsync(recorder, CheckTemplates.ThreePerPage)).Succeeded.ShouldBeTrue();

        // 只画一张的话，"这叠纸一页几张"——选版式时最要紧的那个问题——就看不出来
        recorder.Last!.Checks.Count.ShouldBe(3);
        recorder.Last.Checks.Select(c => c.CheckNumber).ShouldBeUnique();
    }

    [Fact]
    public async Task Specimen_IsMarkedNonNegotiableAndNotAsAPreviewOfSomeRealPayment()
    {
        await SeedCoaAsync();

        var recorder = new RecordingCheckRenderer();
        (await SpecimenAsync(recorder, CheckTemplates.Cpa006Canada)).Succeeded.ShouldBeTrue();

        recorder.Last!.IsPreview.ShouldBeTrue();
        recorder.Last.PreviewLabel.ShouldBe("SPECIMEN - NOT NEGOTIABLE");
        // 样张还要在屏幕上把「票纸自带的」与「打印机现打的」分开 —— 光有水印做不到这件事
        recorder.Last.IsSpecimen.ShouldBeTrue();
    }

    [Fact]
    public async Task PaymentPreview_IsNotASpecimen_SoItKeepsShowingTheFullFace()
    {
        // ★ 刻意的不对称：付款预览屏幕上就该显示完整票面（那时的任务是校对整张支票），
        // 只有样张需要第三种呈现。这条钉住「样张的淡显不会漏进预览」。
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        await CreateBankAccountAsync(ledger);
        var payment = await CreatePostedCheckPaymentAsync(ledger, await CreateVendorAsync(), 100m);

        var recorder = new RecordingCheckRenderer();
        using var scope = ServiceProvider.CreateScope();
        var preview = await BuildService(scope.ServiceProvider, recorder, NewCatalogue())
            .PreviewAsync(new PreviewChecksDto { PaymentEntryIds = new List<Guid> { payment } });

        preview.Succeeded.ShouldBeTrue(preview.Message);
        recorder.Last!.IsPreview.ShouldBeTrue();
        recorder.Last.IsSpecimen.ShouldBeFalse();
    }

    // ── 账号绝不外泄（本轮最要紧的一条）──────────────────────

    [Fact]
    public async Task BlankStockSpecimen_NeverCarriesTheStoredAccountNumber()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(ledger);

        var recorder = new RecordingCheckRenderer();
        var result = await SpecimenAsync(recorder, CheckTemplates.Cpa006Canada, CheckStockType.Blank, bank);
        result.Succeeded.ShouldBeTrue(result.Message);

        // ★ 磁码带要画得出来（否则看不出白纸模式与预印的差别）……
        recorder.Last!.StockType.ShouldBe(CheckStockType.Blank);
        recorder.Last.AccountNumberPlain.ShouldNotBeNullOrWhiteSpace();
        // ……但绝不能是这个账户的真账号：那张纸的磁性编码会与真票无异
        recorder.Last.AccountNumberPlain.ShouldNotBe(RealAccountNumber);
        recorder.Last.AccountNumberPlain.ShouldBe("000000000");
    }

    [Fact]
    public async Task BlankStockSpecimen_NeverEvenDecryptsTheStoredAccountNumber()
    {
        // ★ 上一条断言的是**结果**（请求里不是真账号），而结果同时由两道闸门保证：
        // BuildSpecimenRequest 事后覆盖成占位值，以及 useStoredAccountNumber:false 根本不解密。
        // 只断言结果的话，把后者删掉测试照样全绿（变异验证实测如此）——于是这一条直接
        // 盯着密文有没有被解开：明文压根不该进过内存。
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(ledger);

        var protector = await SpyProtectorAsync();
        using var scope = ServiceProvider.CreateScope();
        var service = BuildService(scope.ServiceProvider, new RecordingCheckRenderer(), NewCatalogue(),
            BuildComposer(scope.ServiceProvider, protector));

        var result = await service.GetTemplateSpecimenAsync(
            CheckTemplates.Cpa006Canada, CheckStockType.Blank, bank);

        result.Succeeded.ShouldBeTrue(result.Message);
        protector.UnprotectCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Printing_DoesDecrypt_SoTheSpyAboveIsNotVacuous()
    {
        // 同一个探针在真打印路径上必须数到解密，否则上一条的 0 只是因为探针没接上。
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        await CreateBankAccountAsync(ledger, CheckStockType.Blank);
        var payment = await CreatePostedCheckPaymentAsync(ledger, await CreateVendorAsync(), 100m);

        var protector = await SpyProtectorAsync();
        using var scope = ServiceProvider.CreateScope();
        var service = BuildService(scope.ServiceProvider, new RecordingCheckRenderer(), NewCatalogue(),
            BuildComposer(scope.ServiceProvider, protector));

        var print = await service.PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment } });

        print.Succeeded.ShouldBeTrue(print.Message);
        protector.UnprotectCalls.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task PrintingTheSameBankAccount_StillUsesTheRealAccountNumber()
    {
        // 变异防护：上面那条是靠「样张这条路径传 useStoredAccountNumber: false」成立的，
        // 不是靠把解密整个关掉——真打印必须照旧拿到真账号，否则支票不可流通。
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(ledger, CheckStockType.Blank);
        var payment = await CreatePostedCheckPaymentAsync(ledger, await CreateVendorAsync(), 100m);

        var recorder = new RecordingCheckRenderer();
        using var scope = ServiceProvider.CreateScope();
        var print = await BuildService(scope.ServiceProvider, recorder, NewCatalogue())
            .PrintAsync(new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment } });

        print.Succeeded.ShouldBeTrue(print.Message);
        recorder.Last!.AccountNumberPlain.ShouldBe(RealAccountNumber);
    }

    /// <summary>
    /// 校准页与样张同口径：既不解密真账号，又照样把磁码带画得出来。
    /// </summary>
    /// <remarks>
    /// 内置的两个校准渲染器只画标尺与磁码带轮廓，用不到账号 —— 但
    /// <c>ICheckDocumentRenderer</c> 是可替换扩展点，换一个会画磁码数字的实现，
    /// 这张会被下载、被打印的纸，其磁性编码就与真票无异。
    /// </remarks>
    [Fact]
    public async Task BlankStockCalibration_NeverDecryptsTheStoredAccountNumber()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(ledger, CheckStockType.Blank);

        var protector = await SpyProtectorAsync();
        var recorder = new RecordingCheckRenderer();
        using var scope = ServiceProvider.CreateScope();
        var service = BuildService(scope.ServiceProvider, recorder, NewCatalogue(),
            BuildComposer(scope.ServiceProvider, protector));

        var result = await service.GetCalibrationPdfAsync(bank);

        result.Succeeded.ShouldBeTrue(result.Message);
        protector.UnprotectCalls.ShouldBe(0, "校准页不需要真账号，也就不该解开它");
        recorder.Last!.AccountNumberPlain.ShouldBe("000000000", "磁码带仍要画得出来，用占位账号");
    }

    // ── 绑定与不绑定档案 ─────────────────────────────────────

    [Fact]
    public async Task WithoutABankAccount_TheSpecimenUsesNeutralPlaceholders()
    {
        await SeedCoaAsync();

        var recorder = new RecordingCheckRenderer();
        (await SpecimenAsync(recorder, CheckTemplates.Cpa006Canada)).Succeeded.ShouldBeTrue();

        recorder.Last!.BankName.ShouldBe("Sample Bank");
        recorder.Last.AccountName.ShouldBe("Sample account");
        // CPA-006 是加拿大版式 → 银行区画 transit/institution 而不是 US routing
        recorder.Last.Scheme.ShouldBe(BankNumberScheme.CaEft);
    }

    [Fact]
    public async Task WithABankAccount_TheSpecimenBorrowsItsIdentityAndAlignment()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(ledger);
        await MutateBankAsync(bank, b =>
        {
            b.BankName = "Bank of the North";
            b.OffsetXMm = 2.5m;
            b.OffsetYMm = -1.25m;
        });

        var recorder = new RecordingCheckRenderer();
        (await SpecimenAsync(recorder, CheckTemplates.Cpa006Canada, bankAccountId: bank)).Succeeded.ShouldBeTrue();

        recorder.Last!.BankName.ShouldBe("Bank of the North");
        recorder.Last.RoutingNumber.ShouldBe("021000021");
        // 偏移跟着档案走：绑了档案的样张要和那台打印机真会印出来的位置一致
        recorder.Last.OffsetXMm.ShouldBe(2.5m);
        recorder.Last.OffsetYMm.ShouldBe(-1.25m);
    }

    // ── 失败形态与目录端点同构 ───────────────────────────────

    [Fact]
    public async Task UnknownLayoutName_Is404_NotARendererCrash()
    {
        await SeedCoaAsync();

        var result = await SpecimenAsync(new RecordingCheckRenderer(), "no-such-layout");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        result.Message.ShouldNotBeNull().ShouldContain("no-such-layout");
    }

    [Fact]
    public async Task MissingBankAccount_Is404()
    {
        await SeedCoaAsync();

        var result = await SpecimenAsync(
            new RecordingCheckRenderer(), CheckTemplates.Cpa006Canada, bankAccountId: Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task WithoutTheRenderingModule_Is501_LikeTheCatalogueEndpoint()
    {
        using var scope = ServiceProvider.CreateScope();
        var service = BuildService(scope.ServiceProvider, renderer: null, catalogue: null);

        var specimen = await service.GetTemplateSpecimenAsync(CheckTemplates.Cpa006Canada);
        var catalogue = await service.GetTemplatesAsync();

        specimen.Code.ShouldBe(501);
        catalogue.Code.ShouldBe(501);
    }

    // ── 缓存友好：同一套版式每次出字节相同的样张 ─────────────

    [Fact]
    public async Task RepeatedCalls_RenderByteIdenticalHtml()
    {
        // 呈现端一次打开就是六次渲染（下拉里每套版式一张缩略图）。样张内容固定
        // （不取当前时间）→ 可缓存。★ 断言打在**真正渲染出来的 HTML** 上：
        // 拿替身渲染器返回的固定字节去比对，比的是替身而不是样张。
        await SeedCoaAsync();

        var first = await RenderSpecimenHtmlAsync(CheckTemplates.Cpa006Canada, CheckStockType.PrePrinted);
        var second = await RenderSpecimenHtmlAsync(CheckTemplates.Cpa006Canada, CheckStockType.PrePrinted);

        first.ShouldBe(second);
    }

    /// <summary>真的 Razor 渲染跑一遍：样张出得来票面，且票纸差别在纸上看得见。</summary>
    [Theory]
    [InlineData(CheckStockType.PrePrinted)]
    [InlineData(CheckStockType.Blank)]
    public async Task Specimen_RendersRealHtmlThroughTheSameModelFactory(CheckStockType stockType)
    {
        await SeedCoaAsync();

        var html = await RenderSpecimenHtmlAsync(CheckTemplates.Cpa006Canada, stockType);

        html.ShouldContain("SPECIMEN - NOT NEGOTIABLE");
        html.ShouldContain("Sample Payee Ltd.");
        html.ShouldContain("***1,234.56");
        html.ShouldContain("Sample field");            // 存根附加行槽位也画出来了
        // 票纸差别看得见：白纸现打磁码，预印票纸上磁码已印在纸上故不重复打
        if (stockType == CheckStockType.Blank)
        {
            html.ShouldContain("class=\"micr-line\"");
            // 磁码里是占位账号，不是那个账户的真账号
            html.ShouldNotContain(RealAccountNumber);
        }
        else
        {
            html.ShouldNotContain("class=\"micr-line\"");
        }
    }

    // ── fixture helpers ──────────────────────────────────────

    private const string RealAccountNumber = "123456789012";

    private static CheckTemplateCatalog NewCatalogue()
        => new(NullLogger<CheckTemplateCatalog>.Instance);

    /// <remarks>
    /// ★ <paramref name="catalogue"/> 不做 <c>?? NewCatalogue()</c> 兜底：那会让
    /// "未加载渲染子模块"的用例根本构造不出缺席状态（第一版就是这么假绿的 —— 目录回了 200）。
    /// </remarks>
    private static CheckService BuildService(
        IServiceProvider sp, ICheckDocumentRenderer? renderer, ICheckTemplateCatalog? catalogue,
        CheckBatchComposer? composer = null)
        => new(
            sp,
            sp.GetRequiredService<IRepository<BankCheck, Guid>>(),
            sp.GetRequiredService<IRepository<BankAccount, Guid>>(),
            sp.GetRequiredService<IRepository<PaymentEntry, Guid>>(),
            sp.GetRequiredService<CheckNumberAllocator>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<FinanceOptions>>(),
            composer ?? sp.GetRequiredService<CheckBatchComposer>(),
            renderer,
            catalogue);

    /// <summary>装一个带探针加密器的 composer（其余协作者仍取容器里的真件）。</summary>
    private static CheckBatchComposer BuildComposer(IServiceProvider sp, IFinanceDataProtector protector)
        => new(
            sp.GetRequiredService<IReadOnlyRepository<PaymentEntry, Guid>>(),
            sp.GetRequiredService<IReadOnlyRepository<BankAccount, Guid>>(),
            sp.GetRequiredService<IReadOnlyRepository<BankCheck, Guid>>(),
            sp.GetRequiredService<IReadOnlyRepository<Vendor, Guid>>(),
            sp.GetRequiredService<CheckIssuerResolver>(),
            protector,
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<FinanceOptions>>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<FinanceCheckOptions>>());

    private Task<CountingProtector> SpyProtectorAsync()
    {
        using var scope = ServiceProvider.CreateScope();
        return Task.FromResult(new CountingProtector(scope.ServiceProvider.GetRequiredService<IFinanceDataProtector>()));
    }

    /// <summary>数一数密文被解开过几次（其余行为原样转交真件）。</summary>
    private sealed class CountingProtector(IFinanceDataProtector inner) : IFinanceDataProtector
    {
        public int UnprotectCalls { get; private set; }

        public bool IsConfigured => inner.IsConfigured;

        public string Protect(string plaintext) => inner.Protect(plaintext);

        public string Protect(string plaintext, string associatedData) => inner.Protect(plaintext, associatedData);

        public string Unprotect(string protectedValue)
        {
            UnprotectCalls++;
            return inner.Unprotect(protectedValue);
        }

        public string Unprotect(string protectedValue, string associatedData)
        {
            UnprotectCalls++;
            return inner.Unprotect(protectedValue, associatedData);
        }
    }

    private async Task<Result<CheckFileDto>> SpecimenAsync(
        RecordingCheckRenderer recorder,
        string templateName,
        CheckStockType stockType = CheckStockType.PrePrinted,
        Guid? bankAccountId = null)
    {
        using var scope = ServiceProvider.CreateScope();
        return await BuildService(scope.ServiceProvider, recorder, NewCatalogue())
            .GetTemplateSpecimenAsync(templateName, stockType, bankAccountId);
    }

    /// <summary>
    /// 取服务真正发给渲染器的那个请求，再经出厂模板正文 + 同一个模型工厂渲染成 HTML。
    /// </summary>
    private async Task<string> RenderSpecimenHtmlAsync(string templateName, CheckStockType stockType)
    {
        using var scope = ServiceProvider.CreateScope();
        var recorder = new RecordingCheckRenderer();
        var result = await BuildService(scope.ServiceProvider, recorder, NewCatalogue())
            .GetTemplateSpecimenAsync(templateName, stockType);
        result.Succeeded.ShouldBeTrue(result.Message);

        return await CheckTemplateHarness.RenderByTemplateNameAsync(templateName, recorder.Last!);
    }

    private async Task<Guid> CreateBankAccountAsync(Guid ledgerId, CheckStockType stock = CheckStockType.PrePrinted)
    {
        var result = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.CreateAsync(new CreateBankAccountDto
        {
            AccountId = ledgerId,
            Name = "Operating",
            Scheme = BankNumberScheme.UsAba,
            RoutingNumber = "021000021",
            AccountNumber = RealAccountNumber,
            NextCheckNumber = 1,
            CheckStockType = stock
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

    private async Task MutateBankAsync(Guid bankAccountId, Action<BankAccount> mutate)
    {
        using var scope = ServiceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<BankAccount, Guid>>();
        var bank = await repository.GetAsync(bankAccountId);
        mutate(bank!);
        await repository.UpdateAsync(bank!);
        await repository.SaveChangesAsync();
    }

    /// <summary>记下渲染器实际收到的请求（本套用例断言的对象就是它）。</summary>
    private sealed class RecordingCheckRenderer : ICheckDocumentRenderer
    {
        public CheckRenderRequest? Last { get; private set; }

        public string ContentType => "text/html";
        public string FileExtension => ".html";

        public Result<byte[]> Render(CheckRenderRequest request)
        {
            Last = request;
            return Result<byte[]>.Success([1]);
        }

        public Result<byte[]> RenderCalibration(CheckRenderRequest request)
        {
            Last = request;
            return Result<byte[]>.Success([1]);
        }
    }
}
