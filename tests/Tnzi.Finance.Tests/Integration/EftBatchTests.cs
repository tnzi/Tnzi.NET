using System.Text;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// P3 块 3：EFT 批次组建 / 加密固化 / 生成不可改 / 作废释放
/// </summary>
public class EftBatchTests : FinanceIntegrationTestBase
{
    /// <summary>
    /// 一个稳定落在未来的生效日。
    /// </summary>
    /// <remarks>
    /// EFT 批次拒绝过去的生效日（见 <c>CreateBatch_PastEffectiveDate_Rejects400</c>），
    /// 所以「正常路径」的测试必须**算**出一个未来日期，而不是写死一个。写死的日期
    /// 是一颗定时炸弹：到期那天四个测试同时变红，且失败信息与被测行为毫无关系。
    /// </remarks>
    private static DateTime FutureDate(int offsetDays = 0) => DateTime.UtcNow.Date.AddDays(7 + offsetDays);

    private Task<Guid> BankLedgerIdAsync() => AccountIdByCodeAsync("1120");

    private async Task<Guid> CreateBankAccountAsync()
    {
        var ledger = await BankLedgerIdAsync();
        var result = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.CreateAsync(new CreateBankAccountDto
        {
            AccountId = ledger,
            Name = "Operating",
            Scheme = BankNumberScheme.UsAba,
            RoutingNumber = "021000021",
            AccountNumber = "111222333",
            EftOriginatorId = "123456789",
            EftOriginatorName = "ACME CORP"
        }));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Id;
    }

    private async Task<Guid> CreateVendorWithBankAsync(string account = "1234567", string name = "Acme Supplies")
    {
        var vendor = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = name }));
        vendor.Succeeded.ShouldBeTrue(vendor.Message);

        var party = await InScopeAsync<IPartyBankAccountService, Result<PartyBankAccountDto>>(s => s.CreateAsync(new SavePartyBankAccountDto
        {
            PartyType = FinancePartyType.Vendor,
            PartyId = vendor.Data!.Id,
            Scheme = BankNumberScheme.UsAba,
            RoutingNumber = "011401533",
            AccountNumber = account,
            IsDefault = true
        }));
        party.Succeeded.ShouldBeTrue(party.Message);
        return vendor.Data!.Id;
    }

    private async Task<Guid> CreatePostedTransferPaymentAsync(Guid ledgerId, Guid vendorId, decimal amount)
    {
        var draft = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Outbound,
            PartyType = FinancePartyType.Vendor,
            PartyId = vendorId,
            DocDate = new DateTime(2026, 7, 10),
            Currency = "USD",
            Amount = amount,
            DepositToAccountId = ledgerId,
            PaymentMethod = PaymentMethods.BankTransfer
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var posted = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        return posted.Data!.Id;
    }

    private Task<Result<EftBatchDto>> CreateBatchAsync(CreateEftBatchDto input)
        => InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.CreateBatchAsync(input));

    /// <summary>
    /// 已生成文件的批次里的付款，不得被作废。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>PaymentEntryService.VoidAsync</c> 在冲销前会调 <c>IFinancePostingGuard</c>，
    /// 但在本次修复之前<b>全仓一个实现都没有</b> —— 空集合上的检查恒成功。而 Finance 核心
    /// 对 <c>EftBatch</c> 的引用数是 <b>0</b>，<c>BankStatementHoldProvider</c> 又只认已匹配的
    /// 银行流水，与 EFT 无关。于是文件已经交给银行的付款可以被静默作废：
    /// 账上冲销了，银行按生效日照付，批次里仍列着它、合计仍含它。
    /// </para>
    /// <para>
    /// 支票侧一直有对称保护（<c>PaymentVoidedCheckHandler</c>），EFT 侧此前一片空白 ——
    /// 而 EFT 是真正把钱送出门且不可撤回的那一条。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task VoidPayment_InGeneratedBatch_IsRejected()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorWithBankAsync(account: "5550001");
        var payment = await CreatePostedTransferPaymentAsync(ledger, vendor, 250m);

        var batch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha, EffectiveDate = FutureDate(),
            PaymentEntryIds = new List<Guid> { payment }
        });
        batch.Succeeded.ShouldBeTrue(batch.Message);
        (await InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.GenerateAsync(batch.Data!.Id)))
            .Succeeded.ShouldBeTrue();

        var voided = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(
            s => s.VoidAsync(payment));

        voided.Succeeded.ShouldBeFalse("文件已生成、可能已送到银行，账上不能单方面冲掉这笔钱");
        voided.Code.ShouldBe(409);

        // 拒绝路径零副作用：付款仍是 Posted，没有产生冲销凭证。
        var reloaded = await ReloadAsync<PaymentEntry>(payment);
        reloaded!.Status.ShouldBe(FinanceDocumentStatus.Posted);
        reloaded.VoidJournalEntryId.ShouldBeNull();
    }

    /// <summary>
    /// 草稿批次里的付款同样不得被作废。
    /// </summary>
    /// <remarks>
    /// <c>GenerateAsync</c> 只校验批次状态，<b>不重新校验行上付款的状态</b> ——
    /// 放行草稿就意味着「作废付款 → 生成文件」会产出一份支付已作废付款的报文。
    /// 两种状态的补救路径本来也一样（没有「移除单行」的操作，只能整批作废后重建）。
    /// </remarks>
    [Fact]
    public async Task VoidPayment_InDraftBatch_IsRejected()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorWithBankAsync(account: "5550002");
        var payment = await CreatePostedTransferPaymentAsync(ledger, vendor, 260m);

        var batch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha, EffectiveDate = FutureDate(),
            PaymentEntryIds = new List<Guid> { payment }
        });
        batch.Succeeded.ShouldBeTrue(batch.Message);

        var voided = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(
            s => s.VoidAsync(payment));

        voided.Succeeded.ShouldBeFalse();
        voided.Code.ShouldBe(409);
    }

    /// <summary>
    /// 作废批次之后，付款恢复可作废 —— 补救路径必须真的走得通。
    /// </summary>
    /// <remarks>
    /// 这条同时防止把守卫做成死路：作废批次是操作员在明确声明「这批没有发出去」，
    /// 是一个有主体、有痕迹的动作，而不是作废一笔付款时悄悄发生的副作用。
    /// </remarks>
    [Fact]
    public async Task VoidPayment_AfterTheBatchIsVoided_Succeeds()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorWithBankAsync(account: "5550003");
        var payment = await CreatePostedTransferPaymentAsync(ledger, vendor, 270m);

        var batch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha, EffectiveDate = FutureDate(),
            PaymentEntryIds = new List<Guid> { payment }
        });
        batch.Succeeded.ShouldBeTrue(batch.Message);
        (await InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.GenerateAsync(batch.Data!.Id)))
            .Succeeded.ShouldBeTrue();

        (await InScopeAsync<IEftService, Result<EftBatchDto>>(
            s => s.VoidBatchAsync(batch.Data!.Id, new VoidEftBatchDto()))).Succeeded.ShouldBeTrue();

        var voided = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(
            s => s.VoidAsync(payment));

        voided.Succeeded.ShouldBeTrue($"批次作废后这笔付款应当可以正常作废：{voided.Message}");
    }

    [Fact]
    public async Task Generate_EncryptsFile_NotPlaintextInDb_Download_Decrypts()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorWithBankAsync(account: "9876543");
        var p = await CreatePostedTransferPaymentAsync(ledger, vendor, 100m);

        var batch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha, EffectiveDate = FutureDate(),
            PaymentEntryIds = new List<Guid> { p }
        });
        batch.Succeeded.ShouldBeTrue(batch.Message);
        batch.Data!.Currency.ShouldBe("USD");
        batch.Data.TotalCount.ShouldBe(1);

        var generated = await InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.GenerateAsync(batch.Data.Id));
        generated.Succeeded.ShouldBeTrue(generated.Message);
        generated.Data!.Status.ShouldBe(EftBatchStatus.Generated);
        generated.Data.Number.ShouldNotBeNull();
        generated.Data.FileCreationNumber.ShouldBe(1);

        // 库中密文非明文
        var entity = await ReloadAsync<EftBatch>(batch.Data.Id);
        entity!.FileContentEncrypted.ShouldNotBeNull();
        entity.FileContentEncrypted!.StartsWith("v1:").ShouldBeTrue();
        entity.FileContentEncrypted.ShouldNotContain("9876543");

        // 下载解密：含收款方路由 + 出款方名
        var download = await InScopeAsync<IEftService, Result<EftFileDto>>(s => s.DownloadAsync(batch.Data.Id));
        download.Succeeded.ShouldBeTrue(download.Message);
        var text = Encoding.UTF8.GetString(download.Data!.Content);
        text.ShouldContain("011401533");
        text.ShouldContain("ACME CORP");
    }

    [Fact]
    public async Task CreateBatch_SchemeMismatch_Rejects400()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync(); // UsAba
        var vendor = await CreateVendorWithBankAsync();
        var p = await CreatePostedTransferPaymentAsync(ledger, vendor, 100m);

        // Cpa005 需要 CaEft 出款行 → scheme 不匹配
        var batch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Cpa005, EffectiveDate = FutureDate(),
            PaymentEntryIds = new List<Guid> { p }
        });
        batch.Succeeded.ShouldBeFalse();
        batch.Code.ShouldBe(400);
    }

    [Fact]
    public async Task CreateBatch_PastEffectiveDate_Rejects400()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorWithBankAsync();
        var p = await CreatePostedTransferPaymentAsync(ledger, vendor, 100m);

        // 生效日在过去 → ODFI 会拒收，前置拒绝 400
        var batch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha, EffectiveDate = DateTime.UtcNow.Date.AddDays(-1),
            PaymentEntryIds = new List<Guid> { p }
        });
        batch.Succeeded.ShouldBeFalse();
        batch.Code.ShouldBe(400);
    }

    [Fact]
    public async Task CreateBatch_DuplicateReBatch_Rejects409()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorWithBankAsync();
        var p = await CreatePostedTransferPaymentAsync(ledger, vendor, 100m);

        var first = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha, EffectiveDate = FutureDate(),
            PaymentEntryIds = new List<Guid> { p }
        });
        first.Succeeded.ShouldBeTrue(first.Message);

        var second = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha, EffectiveDate = FutureDate(1),
            PaymentEntryIds = new List<Guid> { p }
        });
        second.Succeeded.ShouldBeFalse();
        second.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Generate_Immutable_Rejects409()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorWithBankAsync();
        var p = await CreatePostedTransferPaymentAsync(ledger, vendor, 100m);

        var batch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha, EffectiveDate = FutureDate(),
            PaymentEntryIds = new List<Guid> { p }
        });
        var first = await InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.GenerateAsync(batch.Data!.Id));
        first.Succeeded.ShouldBeTrue(first.Message);

        var second = await InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.GenerateAsync(batch.Data!.Id));
        second.Succeeded.ShouldBeFalse();
        second.Code.ShouldBe(409);
    }

    [Fact]
    public async Task VoidBatch_ReleasesForRebatch_And_FileCreationNumberIncrements()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorWithBankAsync();
        var p = await CreatePostedTransferPaymentAsync(ledger, vendor, 100m);

        var batch1 = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha, EffectiveDate = FutureDate(),
            PaymentEntryIds = new List<Guid> { p }
        });
        var gen1 = await InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.GenerateAsync(batch1.Data!.Id));
        gen1.Data!.FileCreationNumber.ShouldBe(1);

        // 作废释放付款
        var voided = await InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.VoidBatchAsync(batch1.Data!.Id, new VoidEftBatchDto { Reason = "Rebuild" }));
        voided.Succeeded.ShouldBeTrue(voided.Message);
        voided.Data!.Status.ShouldBe(EftBatchStatus.Voided);

        // 重入批 + 生成 → FileCreationNumber 递增
        var batch2 = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha, EffectiveDate = FutureDate(2),
            PaymentEntryIds = new List<Guid> { p }
        });
        batch2.Succeeded.ShouldBeTrue(batch2.Message);
        var gen2 = await InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.GenerateAsync(batch2.Data!.Id));
        gen2.Succeeded.ShouldBeTrue(gen2.Message);
        gen2.Data!.FileCreationNumber.ShouldBe(2);

        // 对照：这条路径合法正是因为文件从未交出去过。
        (await ReloadAsync<EftBatch>(batch1.Data!.Id))!.FirstDownloadedTime.ShouldBeNull();
    }

    // ── 文件已交出去之后的作废（重复付款）────────────────────────────────────────

    /// <summary>
    /// 生成 → 下载 → 作废，把批内付款放回待付队列，于是它们看起来从没付过。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 这是 EFT 侧后果最重的一条路径：作废<b>硬删</b>批次行（唯一索引无软删过滤），
    /// 付款随即重新出现在 <c>GetQueueAsync</c> 里，可以再装一批、再生成、再交给银行 ——
    /// <b>付第二次</b>。而第二笔从任何一个界面看都完全正常，第一笔的凭据（批内装了哪几笔）
    /// 也随着硬删一起消失了。
    /// </para>
    /// <para>
    /// 框架无从知道文件有没有真的上传到银行门户，但完全知道它<b>有没有被交出去过</b> ——
    /// 而那正是释放付款从「安全（生成错了重建）」变成「危险（付第二次）」的那条分界线。
    /// 所以判定不是禁止作废，而是<b>要求显式确认</b>：忘记确认的代价是多一次 409，
    /// 默认放行的代价是一笔不需要任何人点头的重复付款。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task VoidBatch_AfterTheFileWasHandedOut_IsRejected()
    {
        var (batchId, _) = await HandedOutBatchAsync();

        var voided = await InScopeAsync<IEftService, Result<EftBatchDto>>(
            s => s.VoidBatchAsync(batchId, new VoidEftBatchDto { Reason = "Rebuild" }));

        voided.Succeeded.ShouldBeFalse("文件已交出去过的批次不得在无确认的情况下作废");
        voided.Code.ShouldBe(409);
        (await ReloadAsync<EftBatch>(batchId))!.Status.ShouldBe(EftBatchStatus.Generated);
    }

    /// <summary>
    /// ★★ 真正要守住的不是那个 409，而是<b>付款没有被放回队列</b>。
    /// </summary>
    /// <remarks>
    /// 只断言返回码，看不出行有没有被删掉 —— 而「行被删掉」才是重复付款的成因。
    /// 这里用「同一笔付款能否再装进第二个批次」当探针：能装进去就说明它已经被释放了。
    /// </remarks>
    [Fact]
    public async Task VoidBatch_AfterTheFileWasHandedOut_DoesNotReleaseThePaymentForRebatch()
    {
        var (batchId, context) = await HandedOutBatchAsync();

        await InScopeAsync<IEftService, Result<EftBatchDto>>(
            s => s.VoidBatchAsync(batchId, new VoidEftBatchDto { Reason = "Rebuild" }));

        var rebatch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = context.BankAccountId, Format = EftFileFormat.Nacha,
            EffectiveDate = FutureDate(2), PaymentEntryIds = new List<Guid> { context.PaymentId }
        });

        rebatch.Succeeded.ShouldBeFalse("作废被拒之后这笔付款仍属于原批次，不得重入新批");
        rebatch.Code.ShouldBe(409);
    }

    /// <summary>对照：显式确认「文件从未提交给银行」之后，作废与释放照常进行。</summary>
    [Fact]
    public async Task VoidBatch_AfterHandout_WithExplicitAcknowledgement_Succeeds()
    {
        var (batchId, context) = await HandedOutBatchAsync();

        var voided = await InScopeAsync<IEftService, Result<EftBatchDto>>(
            s => s.VoidBatchAsync(batchId, new VoidEftBatchDto { Reason = "Never uploaded", AcknowledgeFileNotSubmitted = true }));

        voided.Succeeded.ShouldBeTrue(voided.Message);
        voided.Data!.Status.ShouldBe(EftBatchStatus.Voided);

        var rebatch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = context.BankAccountId, Format = EftFileFormat.Nacha,
            EffectiveDate = FutureDate(2), PaymentEntryIds = new List<Guid> { context.PaymentId }
        });
        rebatch.Succeeded.ShouldBeTrue($"确认之后付款应当被释放：{rebatch.Message}");
    }

    /// <summary>
    /// 下载留痕：首次时间只记最早那一次，次数每次递增。
    /// </summary>
    /// <remarks>
    /// 首次时间会被后续下载覆盖的话，这个字段就答不出「最早什么时候交出去的」——
    /// 而那恰恰是它唯一的用途。
    /// </remarks>
    [Fact]
    public async Task Download_StampsTheFirstHandout_AndCountsWithoutMovingIt()
    {
        var (batchId, _) = await HandedOutBatchAsync();
        var afterFirst = (await ReloadAsync<EftBatch>(batchId))!;
        afterFirst.FirstDownloadedTime.ShouldNotBeNull();
        afterFirst.DownloadCount.ShouldBe(1);

        var second = await InScopeAsync<IEftService, Result<EftFileDto>>(s => s.DownloadAsync(batchId));
        second.Succeeded.ShouldBeTrue(second.Message);

        var afterSecond = (await ReloadAsync<EftBatch>(batchId))!;
        afterSecond.DownloadCount.ShouldBe(2);
        afterSecond.FirstDownloadedTime.ShouldBe(afterFirst.FirstDownloadedTime);
    }

    /// <summary>
    /// ★ 下载不得改动并发标记 —— 否则两个人同时下载会有一个拿到 409。
    /// </summary>
    /// <remarks>
    /// 留痕走 <c>ExecuteUpdate</c> 而不是「加载实体 → 改 → 保存」正是为此：后者要经过变更
    /// 跟踪器，从而参与 <c>IConcurrencyStamp</c> 的乐观并发校验，把一个纯读动作变成会因并发
    /// 而失败的写动作。这条用例是那个取舍的可执行形式。
    /// </remarks>
    [Fact]
    public async Task Download_DoesNotBumpTheConcurrencyStamp()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorWithBankAsync();
        var payment = await CreatePostedTransferPaymentAsync(ledger, vendor, 100m);
        var batch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha,
            EffectiveDate = FutureDate(), PaymentEntryIds = new List<Guid> { payment }
        });
        await InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.GenerateAsync(batch.Data!.Id));
        var before = (await ReloadAsync<EftBatch>(batch.Data!.Id))!.ConcurrencyStamp;

        await InScopeAsync<IEftService, Result<EftFileDto>>(s => s.DownloadAsync(batch.Data!.Id));
        await InScopeAsync<IEftService, Result<EftFileDto>>(s => s.DownloadAsync(batch.Data!.Id));

        (await ReloadAsync<EftBatch>(batch.Data!.Id))!.ConcurrencyStamp.ShouldBe(before);
    }

    /// <summary>
    /// ★★ 同一个 scope 里先下载再作废，守卫同样要拦住。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 交出留痕是 <c>ExecuteUpdate</c> 写的，而 <b><c>ExecuteUpdate</c> 不会更新已跟踪的实体</b>。
    /// 作废那一侧若用带跟踪的读取，EF 的标识解析会把跟踪器里那份
    /// <c>FirstDownloadedTime = null</c> 的旧副本交还回来 —— <b>守卫静默失效</b>。
    /// </para>
    /// <para>
    /// ★ 本用例是自查时补的：另外五条用例每条都经 <c>InScopeAsync</c> 走各自的 scope，
    /// 于是全都读到了新值、全都通过，而**同一个 scope 内**（后台任务、批处理、一次请求里
    /// 连着做两件事）的路径完全没人覆盖。实测拿掉无跟踪读取，这条立刻变红而那五条纹丝不动 ——
    /// 「守卫读到陈旧值于是不再设防」正是不会让任何既有测试变红的那类缺陷。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task VoidBatch_AfterHandoutInTheSameScope_IsStillRejected()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorWithBankAsync();
        var payment = await CreatePostedTransferPaymentAsync(ledger, vendor, 100m);
        var batch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha,
            EffectiveDate = FutureDate(), PaymentEntryIds = new List<Guid> { payment }
        });

        using var scope = ServiceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IEftService>();
        await svc.GenerateAsync(batch.Data!.Id);
        await svc.DownloadAsync(batch.Data!.Id);
        var voided = await svc.VoidBatchAsync(batch.Data!.Id, new VoidEftBatchDto { Reason = "probe" });

        voided.Succeeded.ShouldBeFalse("同一 scope 内的作废同样必须被守卫拦住");
        voided.Code.ShouldBe(409);
    }

    /// <summary>生成并下载过一次文件的批次（= 已交出去）。</summary>
    private async Task<(Guid BatchId, (Guid BankAccountId, Guid PaymentId) Context)> HandedOutBatchAsync()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorWithBankAsync();
        var payment = await CreatePostedTransferPaymentAsync(ledger, vendor, 100m);

        var batch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha,
            EffectiveDate = FutureDate(), PaymentEntryIds = new List<Guid> { payment }
        });
        batch.Succeeded.ShouldBeTrue(batch.Message);

        var generated = await InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.GenerateAsync(batch.Data!.Id));
        generated.Succeeded.ShouldBeTrue(generated.Message);

        var download = await InScopeAsync<IEftService, Result<EftFileDto>>(s => s.DownloadAsync(batch.Data!.Id));
        download.Succeeded.ShouldBeTrue(download.Message);

        return (batch.Data!.Id, (bank, payment));
    }
}
