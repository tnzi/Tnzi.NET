using Microsoft.EntityFrameworkCore;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 存根附加行：从消费应用的 <see cref="ICheckStubLineProvider"/> 到渲染请求的整条接线
/// </summary>
/// <remarks>
/// ★ 走 <see cref="ICheckService"/> 真实入口并<b>捕获渲染器实际收到的请求</b>：
/// 纯函数层的边界断言在 <c>CheckStubLineLimitsTests</c>，它单独全绿证明不了这里任何一条
/// （这个仓库反复兑现过的教训：机制建好了没接上）。
/// <para>
/// 重点是<b>重打与重新渲染这两条重建路径</b>：附加行刻意不落库，
/// 靠"拿着同样的付款单 id 再问一次"成立。那条设计只有在这里才被证明。
/// </para>
/// </remarks>
public class CheckStubLineTests : FinanceIntegrationTestBase
{
    private Task<Guid> BankLedgerIdAsync() => AccountIdByCodeAsync("1120");

    [Fact]
    public async Task ProviderLines_ReachTheRendererPerCheck()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(ledger);
        var vendor = await CreateVendorAsync();
        var first = await CreatePostedCheckPaymentAsync(ledger, vendor, 100m);
        var second = await CreatePostedCheckPaymentAsync(ledger, vendor, 250m);

        // 一张支票一组行：这正是律所"一个档案一张票"的形状
        var provider = new StubProvider
        {
            [first] = [new CheckStubLine("File No.", "2026-0042"), new CheckStubLine("Client", "Northwind")],
            [second] = [new CheckStubLine("File No.", "2026-0099")]
        };

        var recorder = new RecordingCheckRenderer();
        var print = await PrintAsync(recorder, provider, new PrintChecksDto
        {
            PaymentEntryIds = new List<Guid> { first, second }
        });
        print.Succeeded.ShouldBeTrue(print.Message);

        provider.AskedFor.ShouldBe(1, "批量索取，逐张回调就是一次 N+1");

        var rendered = recorder.Last!.Checks;
        rendered.Count.ShouldBe(2);
        // 行跟着各自那张票走，不是整批共用一组
        rendered.Single(c => c.Amount == 100m).StubLines
            .Select(l => l.Value).ShouldBe(new[] { "2026-0042", "Northwind" });
        rendered.Single(c => c.Amount == 250m).StubLines
            .ShouldHaveSingleItem().Value.ShouldBe("2026-0099");
    }

    [Fact]
    public async Task NoProviderRegistered_LeavesEveryCheckWithoutStubLines()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        await CreateBankAccountAsync(ledger);
        var payment = await CreatePostedCheckPaymentAsync(ledger, await CreateVendorAsync(), 100m);

        var recorder = new RecordingCheckRenderer();
        var print = await PrintAsync(recorder, provider: null, new PrintChecksDto
        {
            PaymentEntryIds = new List<Guid> { payment }
        });
        print.Succeeded.ShouldBeTrue(print.Message);

        // 未注册 = 没有附加行 = 存根按出厂样子排（与本机制引入前逐字相同）
        recorder.Last!.Checks.ShouldHaveSingleItem().StubLines.ShouldBeEmpty();
    }

    [Fact]
    public async Task PaymentWithoutAnEntryInTheProvidersAnswer_JustHasNoStubLines()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        await CreateBankAccountAsync(ledger);
        var payment = await CreatePostedCheckPaymentAsync(ledger, await CreateVendorAsync(), 100m);

        // 提供者不必为每个 id 都给出条目
        var recorder = new RecordingCheckRenderer();
        var print = await PrintAsync(recorder, new StubProvider(), new PrintChecksDto
        {
            PaymentEntryIds = new List<Guid> { payment }
        });

        print.Succeeded.ShouldBeTrue(print.Message);
        recorder.Last!.Checks.ShouldHaveSingleItem().StubLines.ShouldBeEmpty();
    }

    [Fact]
    public async Task AFailingProvider_DoesNotStopThePaymentFromPrinting()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(ledger);
        var payment = await CreatePostedCheckPaymentAsync(ledger, await CreateVendorAsync(), 100m);

        var recorder = new RecordingCheckRenderer();
        var print = await PrintAsync(recorder, new ThrowingStubProvider(), new PrintChecksDto
        {
            PaymentEntryIds = new List<Guid> { payment }
        });

        // ★ 支票号已分配、登记簿行已写下；拿不到装饰性的附加行不该让付款打不出来
        print.Succeeded.ShouldBeTrue(print.Message);
        recorder.Last!.Checks.ShouldHaveSingleItem().StubLines.ShouldBeEmpty();
        (await SingleCheckAsync(bank)).CheckNumber.ShouldBe(1);
    }

    [Fact]
    public async Task PreviewAndPrint_AskTheSameQuestion_SoTheStubPreviewIsHonest()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        await CreateBankAccountAsync(ledger);
        var payment = await CreatePostedCheckPaymentAsync(ledger, await CreateVendorAsync(), 100m);
        var provider = new StubProvider { [payment] = [new CheckStubLine("Matter", "Estate of Doe")] };

        var previewRecorder = new RecordingCheckRenderer();
        (await PreviewAsync(previewRecorder, provider, new PreviewChecksDto { PaymentEntryIds = new List<Guid> { payment } }))
            .Succeeded.ShouldBeTrue();

        var printRecorder = new RecordingCheckRenderer();
        (await PrintAsync(printRecorder, provider, new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment } }))
            .Succeeded.ShouldBeTrue();

        // "所见即将打"对存根也成立
        previewRecorder.Last!.Checks.Single().StubLines
            .ShouldBe(printRecorder.Last!.Checks.Single().StubLines);
    }

    [Fact]
    public async Task ReRenderingAnIssuedCheck_AsksAgainAndGetsTheStubLinesBack()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(ledger);
        var payment = await CreatePostedCheckPaymentAsync(ledger, await CreateVendorAsync(), 100m);
        var provider = new StubProvider { [payment] = [new CheckStubLine("File No.", "2026-0042")] };

        var recorder = new RecordingCheckRenderer();
        (await PrintAsync(recorder, provider, new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment } }))
            .Succeeded.ShouldBeTrue();
        var issued = await SingleCheckAsync(bank);

        // ★ 框架为附加行落库的是**零个字节**。重新渲染仍然拿到它们，
        // 是因为框架拿着同一个付款单 id 又问了消费应用一次。
        var render = await RenderAsync(recorder, provider, issued.Id);
        render.Succeeded.ShouldBeTrue(render.Message);

        recorder.Last!.Checks.ShouldHaveSingleItem().StubLines
            .ShouldHaveSingleItem().Value.ShouldBe("2026-0042");
    }

    [Fact]
    public async Task ReRendering_FollowsTheApplicationsCurrentAnswer_NotTheOneFromPrintTime()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(ledger);
        var payment = await CreatePostedCheckPaymentAsync(ledger, await CreateVendorAsync(), 100m);
        var provider = new StubProvider { [payment] = [new CheckStubLine("Client", "Northwind Ltd.")] };

        var recorder = new RecordingCheckRenderer();
        (await PrintAsync(recorder, provider, new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment } }))
            .Succeeded.ShouldBeTrue();
        var issued = await SingleCheckAsync(bank);

        // 客户在消费应用那侧改了名
        provider[payment] = [new CheckStubLine("Client", "Northwind Holdings Ltd.")];

        (await RenderAsync(recorder, provider, issued.Id)).Succeeded.ShouldBeTrue();

        // ★ 这是「不持久化」的**代价**，写在明处：存根跟着消费应用的当前答案走，
        // 而票面本身（号 / 收款人 / 金额 / 币种 / 日期）仍取登记簿快照、版式仍取版式快照。
        // 附加行是"这张票为什么开"的注解，不是票据要素；域记录改名之后，
        // 让注解跟着改比留住一个消费应用自己都已经不认的旧值更说得通。
        recorder.Last!.Checks.Single().StubLines
            .ShouldHaveSingleItem().Value.ShouldBe("Northwind Holdings Ltd.");
    }

    [Fact]
    public async Task Reprinting_CarriesTheStubLinesOntoTheReplacementSheet()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(ledger);
        var payment = await CreatePostedCheckPaymentAsync(ledger, await CreateVendorAsync(), 100m);
        var provider = new StubProvider { [payment] = [new CheckStubLine("File No.", "2026-0042")] };

        var recorder = new RecordingCheckRenderer();
        (await PrintAsync(recorder, provider, new PrintChecksDto { PaymentEntryIds = new List<Guid> { payment } }))
            .Succeeded.ShouldBeTrue();
        var original = await SingleCheckAsync(bank);

        var reprint = await ReprintAsync(recorder, provider, original.Id);
        reprint.Succeeded.ShouldBeTrue(reprint.Message);

        // 重打出的是一张新号的票，存根上的档案信息不该因为换了号就消失
        recorder.Last!.Checks.ShouldHaveSingleItem().StubLines
            .ShouldHaveSingleItem().Value.ShouldBe("2026-0042");
    }

    [Fact]
    public async Task AdHocPreview_CarriesStubLinesOnTheRequestAndNormalisesThem()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        await CreateBankAccountAsync(ledger);
        var vendor = await CreateVendorAsync();

        // 这条路径上还没有付款单，无键可查，故由请求直接携带
        var recorder = new RecordingCheckRenderer();
        var tooMany = Enumerable.Range(1, CheckStubLineLimits.MaxLines + 4)
            .Select(i => new CheckStubLine($"Field {i}", $"Value {i}"))
            .ToList();

        using var scope = ServiceProvider.CreateScope();
        var preview = await BuildService(scope.ServiceProvider, recorder, provider: null)
            .PreviewAdHocAsync(new AdHocCheckPreviewDto
            {
                FundsAccountId = ledger,
                Items =
                [
                    new AdHocCheckItemDto
                    {
                        PayeeVendorId = vendor,
                        Amount = 100m,
                        StubLines = tooMany
                    }
                ]
            });

        preview.Succeeded.ShouldBeTrue(preview.Message);
        // 请求 DTO 是外部输入：条数不能由调用方决定票面排不排得下
        recorder.Last!.Checks.ShouldHaveSingleItem().StubLines.Count.ShouldBe(CheckStubLineLimits.MaxLines);
    }

    // ── fixture helpers ──────────────────────────────────────

    /// <summary>
    /// 手工装配 <see cref="CheckService"/>：本套用例要观察渲染器<b>收到了什么</b>，
    /// 而基类固定绑定的是出真 PDF 的 PdfSharp 渲染器（沿用同目录既有用例的做法）。
    /// 存根提供者是 <see cref="CheckBatchComposer"/> 的可选构造参数，故连它一起手工装配。
    /// </summary>
    private static CheckService BuildService(IServiceProvider sp, ICheckDocumentRenderer renderer, ICheckStubLineProvider? provider)
        => new(
            sp,
            sp.GetRequiredService<IRepository<BankCheck, Guid>>(),
            sp.GetRequiredService<IRepository<BankAccount, Guid>>(),
            sp.GetRequiredService<IRepository<PaymentEntry, Guid>>(),
            sp.GetRequiredService<CheckNumberAllocator>(),
            sp.GetRequiredService<IOptionsSnapshot<FinanceOptions>>(),
            BuildComposer(sp, provider),
            renderer);

    private static CheckBatchComposer BuildComposer(IServiceProvider sp, ICheckStubLineProvider? provider)
        => new(
            sp.GetRequiredService<IReadOnlyRepository<PaymentEntry, Guid>>(),
            sp.GetRequiredService<IReadOnlyRepository<BankAccount, Guid>>(),
            sp.GetRequiredService<IReadOnlyRepository<BankCheck, Guid>>(),
            sp.GetRequiredService<IReadOnlyRepository<Vendor, Guid>>(),
            sp.GetRequiredService<CheckIssuerResolver>(),
            sp.GetRequiredService<IFinanceDataProtector>(),
            sp.GetRequiredService<IOptionsSnapshot<FinanceOptions>>(),
            sp.GetRequiredService<IOptionsSnapshot<FinanceCheckOptions>>(),
            provider);

    private async Task<Result<CheckFileDto>> PrintAsync(
        RecordingCheckRenderer recorder, ICheckStubLineProvider? provider, PrintChecksDto input)
    {
        using var scope = ServiceProvider.CreateScope();
        return await BuildService(scope.ServiceProvider, recorder, provider).PrintAsync(input);
    }

    private async Task<Result<CheckFileDto>> PreviewAsync(
        RecordingCheckRenderer recorder, ICheckStubLineProvider? provider, PreviewChecksDto input)
    {
        using var scope = ServiceProvider.CreateScope();
        return await BuildService(scope.ServiceProvider, recorder, provider).PreviewAsync(input);
    }

    private async Task<Result<CheckFileDto>> RenderAsync(
        RecordingCheckRenderer recorder, ICheckStubLineProvider? provider, Guid checkId)
    {
        using var scope = ServiceProvider.CreateScope();
        return await BuildService(scope.ServiceProvider, recorder, provider).RenderAsync(checkId);
    }

    private async Task<Result<CheckFileDto>> ReprintAsync(
        RecordingCheckRenderer recorder, ICheckStubLineProvider? provider, Guid checkId)
    {
        using var scope = ServiceProvider.CreateScope();
        return await BuildService(scope.ServiceProvider, recorder, provider).ReprintAsync(checkId);
    }

    private async Task<Guid> CreateBankAccountAsync(Guid ledgerId)
    {
        var result = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.CreateAsync(new CreateBankAccountDto
        {
            AccountId = ledgerId,
            Name = "Operating",
            Scheme = BankNumberScheme.UsAba,
            RoutingNumber = "021000021",
            AccountNumber = "123456789012",
            NextCheckNumber = 1,
            CheckStockType = CheckStockType.PrePrinted
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

    private async Task<BankCheck> SingleCheckAsync(Guid bankAccountId)
    {
        using var scope = ServiceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<BankCheck, Guid>>();
        return (await repository.AsQueryable()
            .Where(c => c.BankAccountId == bankAccountId && c.Status == CheckStatus.Issued)
            .ToListAsync()).ShouldHaveSingleItem();
    }

    /// <summary>消费应用那一侧的存根行来源（测试替身：一张按付款单 id 索引的表）。</summary>
    private sealed class StubProvider : Dictionary<Guid, IReadOnlyList<CheckStubLine>>, ICheckStubLineProvider
    {
        /// <summary>被问过几次 —— 用来证明这是一次批量提问而不是逐张回调。</summary>
        public int AskedFor { get; private set; }

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<CheckStubLine>>> GetStubLinesAsync(
            IReadOnlyList<Guid> paymentEntryIds, CancellationToken cancellationToken = default)
        {
            AskedFor++;
            IReadOnlyDictionary<Guid, IReadOnlyList<CheckStubLine>> answer =
                paymentEntryIds.Where(ContainsKey).ToDictionary(id => id, id => this[id]);
            return Task.FromResult(answer);
        }
    }

    /// <summary>坏掉的提供者：它不该把一整批付款拖下水。</summary>
    private sealed class ThrowingStubProvider : ICheckStubLineProvider
    {
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<CheckStubLine>>> GetStubLinesAsync(
            IReadOnlyList<Guid> paymentEntryIds, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the application's matter table is unreachable");
    }

    /// <summary>记下渲染器实际收到的请求（本套用例断言的对象就是它）。</summary>
    private sealed class RecordingCheckRenderer : ICheckDocumentRenderer
    {
        public CheckRenderRequest? Last { get; private set; }

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
