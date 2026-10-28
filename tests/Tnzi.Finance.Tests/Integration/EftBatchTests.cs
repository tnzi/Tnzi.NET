using System.Text;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// P3 块 3：EFT 批次组建 / 加密固化 / 生成不可改 / 作废释放
/// </summary>
public class EftBatchTests : FinanceIntegrationTestBase
{
    private readonly RaceHooks _hooks = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        // 后注册者胜：两个钩子默认不挂任何动作，只在竞态用例里各插一次「另一个操作员」。
        services.AddScoped<IFinanceDataProtector>(sp =>
            new HookedProtector(ActivatorUtilities.CreateInstance<FinanceDataProtector>(sp), _hooks));
        services.AddScoped<IRepository<EftBatchLine, Guid>>(sp =>
            new HookedLineRepository(sp.GetRequiredService<FinanceTestDbContext>(), sp, _hooks));
        services.AddScoped<IReadOnlyRepository<EftBatchLine, Guid>>(sp => sp.GetRequiredService<IRepository<EftBatchLine, Guid>>());
    }

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

    private async Task<Guid> CreateVendorWithBankAsync(string account = "1234567", string name = "Acme Supplies", string? routingNumber = "011401533")
    {
        var vendor = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = name }));
        vendor.Succeeded.ShouldBeTrue(vendor.Message);

        var party = await InScopeAsync<IPartyBankAccountService, Result<PartyBankAccountDto>>(s => s.CreateAsync(new SavePartyBankAccountDto
        {
            PartyType = FinancePartyType.Vendor,
            PartyId = vendor.Data!.Id,
            Scheme = BankNumberScheme.UsAba,
            RoutingNumber = routingNumber,
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
    /// <summary>装进批次的付款离开待付队列，没装的留在队列里。</summary>
    [Fact]
    public async Task Queue_ExcludesPaymentsAlreadyInABatch()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorWithBankAsync(account: "5550002");
        var batched = await CreatePostedTransferPaymentAsync(ledger, vendor, 250m);
        var waiting = await CreatePostedTransferPaymentAsync(ledger, vendor, 75m);

        var batch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha, EffectiveDate = FutureDate(),
            PaymentEntryIds = new List<Guid> { batched }
        });
        batch.Succeeded.ShouldBeTrue(batch.Message);

        var queue = await InScopeAsync<IEftService, Result<List<EftQueueItemDto>>>(s => s.GetQueueAsync());
        queue.Succeeded.ShouldBeTrue(queue.Message);
        var item = queue.Data!.Single();
        item.PaymentEntryId.ShouldBe(waiting);
        item.Amount.ShouldBe(75m);
        item.FundingAccountId.ShouldBe(ledger);
    }

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

    // ── 存储的账号解不开（密钥更换、库恢复到另一把密钥的环境） ─────────────────
    //
    // 解密异常此前一路冒成 500，看不出是哪个档案要重新录入账号；NACHA 批次还会因为一个
    // 根本不写进文件的本方账号一起生成不了。拒绝必须发生在分配批次号与文件序号之前。

    /// <summary>一段格式合法、但用任何密钥都解不开的密文（认证标签对不上）。</summary>
    private static string Undecryptable() => "v2:" + Convert.ToBase64String(new byte[40]);

    private Task CorruptAsync<TEntity>(Guid id, Action<TEntity> corrupt) where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
        => InScopeAsync<IRepository<TEntity, Guid>, int>(async repo =>
        {
            var row = (await repo.GetAsync(id))!;
            corrupt(row);
            await repo.UpdateAsync(row);
            await repo.SaveChangesAsync();
            return 0;
        });

    [Fact]
    public async Task Generate_PayeeAccountUndecryptable_Returns400NamingThePayee_AndAllocatesNothing()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorWithBankAsync(account: "5550003", name: "Globex Trading");
        var payment = await CreatePostedTransferPaymentAsync(ledger, vendor, 120m);
        var batch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha, EffectiveDate = FutureDate(),
            PaymentEntryIds = new List<Guid> { payment }
        });
        batch.Succeeded.ShouldBeTrue(batch.Message);
        var partyBank = await InScopeAsync<IReadOnlyRepository<PartyBankAccount, Guid>, PartyBankAccount?>(
            r => r.FirstOrDefaultAsync(a => a.PartyId == vendor));
        await CorruptAsync<PartyBankAccount>(partyBank!.Id, a => a.AccountNumberEncrypted = Undecryptable());

        var generated = await InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.GenerateAsync(batch.Data!.Id));

        generated.Succeeded.ShouldBeFalse();
        generated.Code.ShouldBe(400);
        generated.Message!.ShouldContain("Globex Trading");
        var reloaded = (await ReloadAsync<EftBatch>(batch.Data!.Id))!;
        reloaded.Status.ShouldBe(EftBatchStatus.Draft);
        reloaded.Number.ShouldBeNull();
        reloaded.FileCreationNumber.ShouldBeNull();
        (await ReloadAsync<BankAccount>(bank))!.EftFileCreationNumber.ShouldBe(0);
    }

    /// <summary>NACHA 不写本方账号：本方账号解不开不该挡住 NACHA 文件。</summary>
    [Fact]
    public async Task Generate_Nacha_WithAnUndecryptableOriginatorAccount_StillSucceeds()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorWithBankAsync(account: "5550004");
        var payment = await CreatePostedTransferPaymentAsync(ledger, vendor, 80m);
        var batch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha, EffectiveDate = FutureDate(),
            PaymentEntryIds = new List<Guid> { payment }
        });
        batch.Succeeded.ShouldBeTrue(batch.Message);
        await CorruptAsync<BankAccount>(bank, b => b.AccountNumberEncrypted = Undecryptable());

        var generated = await InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.GenerateAsync(batch.Data!.Id));

        generated.Succeeded.ShouldBeTrue(generated.Message);
        generated.Data!.Status.ShouldBe(EftBatchStatus.Generated);
    }

    /// <summary>CPA-005 要写本方账号：解不开时点名银行账户档案拒绝，不分配任何号。</summary>
    [Fact]
    public async Task Generate_Cpa005_WithAnUndecryptableOriginatorAccount_Returns400NamingTheBankAccount()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var created = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.CreateAsync(new CreateBankAccountDto
        {
            AccountId = ledger,
            Name = "Operating CAD",
            Scheme = BankNumberScheme.CaEft,
            InstitutionNumber = "003",
            TransitNumber = "12345",
            AccountNumber = "1234567",
            EftOriginatorId = "ACMECORP01",
            EftOriginatorName = "ACME CORP"
        }));
        created.Succeeded.ShouldBeTrue(created.Message);
        var bank = created.Data!.Id;
        await CorruptAsync<BankAccount>(bank, b => b.AccountNumberEncrypted = Undecryptable());

        // 直接落一个草稿批次：本用例只关心生成时对本方账号的处理，不关心装批路径上的币种与收款方。
        var batchId = Guid.NewGuid();
        await InScopeAsync<IServiceProvider, int>(async sp =>
        {
            var batches = sp.GetRequiredService<IRepository<EftBatch, Guid>>();
            await batches.InsertAsync(new EftBatch
            {
                Id = batchId, BankAccountId = bank, Format = EftFileFormat.Cpa005, Currency = "CAD",
                EffectiveDate = FutureDate(), Status = EftBatchStatus.Draft, TotalCount = 1, TotalAmount = 1m
            });
            await batches.SaveChangesAsync();
            var lines = sp.GetRequiredService<IRepository<EftBatchLine, Guid>>();
            await lines.InsertAsync(new EftBatchLine
            {
                EftBatchId = batchId, PaymentEntryId = Guid.NewGuid(), PartyBankAccountId = Guid.NewGuid(), Amount = 1m, PayeeName = "Payee"
            });
            await lines.SaveChangesAsync();
            return 0;
        });

        var generated = await InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.GenerateAsync(batchId));

        generated.Succeeded.ShouldBeFalse();
        generated.Code.ShouldBe(400);
        generated.Message!.ShouldContain("Operating CAD");
        (await ReloadAsync<EftBatch>(batchId))!.Number.ShouldBeNull();
        (await ReloadAsync<BankAccount>(bank))!.EftFileCreationNumber.ShouldBe(0);
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

    /// <summary>
    /// 付款贷的是哪个资金科目，文件就必须从那个科目对应的银行档案出钱。
    /// </summary>
    /// <remarks>
    /// 付款过账贷记 <c>DepositToAccountId</c>（这里是 1120）；若操作员在装批时选了挂在
    /// 另一个科目（1110）上的档案，生成的文件让银行从 1110 对应的账户付钱，而总账记的是
    /// 1120 减少，两个账户的银行对账同时对不平，此前每一步都返回成功。
    /// 拒绝必须发生在任何写入之前：批次表零行，付款仍在待付队列里。
    /// </remarks>
    [Fact]
    public async Task CreateBatch_PaymentFundedFromAnotherLedgerAccount_Rejects400_AndWritesNothing()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        await CreateBankAccountAsync(); // 1120 上的档案：付款真正的出款户
        var otherLedger = await AccountIdByCodeAsync("1110");
        var other = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.CreateAsync(new CreateBankAccountDto
        {
            AccountId = otherLedger,
            Name = "Payroll",
            Scheme = BankNumberScheme.UsAba,
            RoutingNumber = "021000021",
            AccountNumber = "999888777",
            EftOriginatorId = "123456789",
            EftOriginatorName = "ACME CORP"
        }));
        other.Succeeded.ShouldBeTrue(other.Message);
        var vendor = await CreateVendorWithBankAsync(account: "5550101");
        var payment = await CreatePostedTransferPaymentAsync(ledger, vendor, 125m);

        var batch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = other.Data!.Id, Format = EftFileFormat.Nacha, EffectiveDate = FutureDate(),
            PaymentEntryIds = new List<Guid> { payment }
        });

        batch.Succeeded.ShouldBeFalse("the file would debit the Payroll account while the ledger credited the Operating account");
        batch.Code.ShouldBe(400);
        batch.Message.ShouldNotBeNull().ShouldContain("not funded from the selected bank account");

        (await InScopeAsync<IReadOnlyRepository<EftBatch, Guid>, int>(r => r.CountAsync())).ShouldBe(0);
        (await InScopeAsync<IReadOnlyRepository<EftBatchLine, Guid>, int>(r => r.CountAsync())).ShouldBe(0);

        // 队列把出款科目带出来，呈现端据此只列匹配的档案；付款仍在队列里可以正确地重新装批。
        var queue = await InScopeAsync<IEftService, Result<List<EftQueueItemDto>>>(s => s.GetQueueAsync());
        queue.Succeeded.ShouldBeTrue(queue.Message);
        var item = queue.Data!.Single(q => q.PaymentEntryId == payment);
        item.FundingAccountId.ShouldBe(ledger);
    }

    [Fact]
    public async Task CreateBatch_PastEffectiveDate_Rejects400()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorWithBankAsync();
        var p = await CreatePostedTransferPaymentAsync(ledger, vendor, 100m);

        // 生效日在过去 → ODFI 会拒收，前置拒绝 400。用前天：「昨天」在 UTC-12 那一侧可能还是今天，
        // 判据只拒绝在任何时区都已经过去的日期（见 EftEffectiveDateTests）。
        var batch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha, EffectiveDate = DateTime.UtcNow.Date.AddDays(-2),
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

    /// <summary>
    /// 收款方档案没有完整路由号 → 装批当场拒绝，而不是等到生成文件。
    /// </summary>
    /// <remarks>
    /// 档案允许只登记名称与账号（路由留空是合法的），但这样的档案<b>装不进 EFT 文件</b>：
    /// 定宽写入器会把空路由补成全零，产出一份语法合法、指向不存在机构的报文。
    /// 在装批时拒绝才点得出是哪一笔付款；留到生成阶段，操作员拿到的是一条与他当时
    /// 在做的事已经对不上的报错。
    /// </remarks>
    [Fact]
    public async Task CreateBatch_PayeeWithoutRouting_Rejects400()
    {
        await SeedCoaAsync();
        var ledger = await BankLedgerIdAsync();
        var bank = await CreateBankAccountAsync();
        var vendor = await CreateVendorWithBankAsync(account: "5550009", routingNumber: null);
        var payment = await CreatePostedTransferPaymentAsync(ledger, vendor, 300m);

        var batch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha, EffectiveDate = FutureDate(),
            PaymentEntryIds = new List<Guid> { payment }
        });

        batch.Succeeded.ShouldBeFalse("路由留空的档案装不进 EFT 文件");
        batch.Code.ShouldBe(400);
        batch.Message!.ShouldContain("routing");
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
    /// ★ 两个人同时下载都成功、都计数：留痕是条件 <c>ExecuteUpdate</c>，下载之间不做带跟踪的比对。
    /// </summary>
    /// <remarks>
    /// 留痕走 <c>ExecuteUpdate</c> 而不是「加载实体 → 改 → 保存」正是为此：后者要经过变更
    /// 跟踪器，从而对 <c>IConcurrencyStamp</c> 做乐观并发比对，把一个纯读动作变成会因并发
    /// 而失败的写动作。第二次下载插在第一次的「读到 Generated」与「留痕」之间。
    /// </remarks>
    [Fact]
    public async Task Download_ConcurrentWithAnotherDownload_BothSucceed_AndBothCount()
    {
        var batchId = await GeneratedBatchAsync();
        Result<EftFileDto>? inner = null;
        _hooks.BeforeUnprotect = async () =>
            inner = await InScopeAsync<IEftService, Result<EftFileDto>>(s => s.DownloadAsync(batchId));

        var outer = await InScopeAsync<IEftService, Result<EftFileDto>>(s => s.DownloadAsync(batchId));

        inner.ShouldNotBeNull("钩子必须真的插进了第一次下载");
        inner.Succeeded.ShouldBeTrue(inner.Message);
        outer.Succeeded.ShouldBeTrue(outer.Message);
        (await ReloadAsync<EftBatch>(batchId))!.DownloadCount.ShouldBe(2);
    }

    /// <summary>
    /// ★★ 下载读到 Generated 之后批次被并发作废：文件不得交出。
    /// </summary>
    /// <remarks>
    /// 作废已经硬删了批次行、把付款放回待付队列；这时把文件交出去，那些付款会被再装一批付第二次：
    /// 恰恰是作废守卫要挡的那笔重复付款，只是从另一侧绕了过去。
    /// </remarks>
    [Fact]
    public async Task Download_WhenTheBatchIsVoidedAfterTheStatusRead_Returns409_AndHandsOutNoFile()
    {
        var batchId = await GeneratedBatchAsync();
        Result<EftBatchDto>? voided = null;
        _hooks.BeforeUnprotect = async () =>
            voided = await InScopeAsync<IEftService, Result<EftBatchDto>>(
                s => s.VoidBatchAsync(batchId, new VoidEftBatchDto { Reason = "Concurrent void" }));

        var download = await InScopeAsync<IEftService, Result<EftFileDto>>(s => s.DownloadAsync(batchId));

        voided.ShouldNotBeNull("钩子必须真的插进了下载");
        voided.Succeeded.ShouldBeTrue(voided.Message);
        download.Succeeded.ShouldBeFalse("批次已被作废，文件不得交出");
        download.Code.ShouldBe(409);
        download.Data.ShouldBeNull();
        var reloaded = (await ReloadAsync<EftBatch>(batchId))!;
        reloaded.Status.ShouldBe(EftBatchStatus.Voided);
        reloaded.FirstDownloadedTime.ShouldBeNull();
        reloaded.DownloadCount.ShouldBe(0);
    }

    /// <summary>
    /// ★★ 作废读到「从未下载」之后文件被交出：作废必须失败，付款留在原批次，留痕保住。
    /// </summary>
    /// <remarks>
    /// 作废的守卫读与提交之间插进一次下载。此前作废照常提交：行被删、付款回到队列，
    /// 而它那次整行写入还把刚戳上的 <c>FirstDownloadedTime</c> 写回 null，连「交出去过」的痕迹都抹掉。
    /// </remarks>
    [Fact]
    public async Task Void_WhenTheFileIsHandedOutAfterTheGuardRead_Returns409_AndKeepsTheBatch()
    {
        var batchId = await GeneratedBatchAsync();
        var paymentId = await InScopeAsync<IReadOnlyRepository<EftBatchLine, Guid>, Guid>(
            async r => (await r.FirstOrDefaultAsync(l => l.EftBatchId == batchId))!.PaymentEntryId);
        var bankAccountId = (await ReloadAsync<EftBatch>(batchId))!.BankAccountId;
        Result<EftFileDto>? download = null;
        _hooks.BeforeLineLoad = async () =>
            download = await InScopeAsync<IEftService, Result<EftFileDto>>(s => s.DownloadAsync(batchId));

        var voided = await InScopeAsync<IEftService, Result<EftBatchDto>>(
            s => s.VoidBatchAsync(batchId, new VoidEftBatchDto { Reason = "Concurrent void" }));

        download.ShouldNotBeNull("钩子必须真的插进了作废");
        download.Succeeded.ShouldBeTrue(download.Message);
        voided.Succeeded.ShouldBeFalse("文件已在作废提交前交出，作废必须失败");
        voided.Code.ShouldBe(409);
        var reloaded = (await ReloadAsync<EftBatch>(batchId))!;
        reloaded.Status.ShouldBe(EftBatchStatus.Generated);
        reloaded.FirstDownloadedTime.ShouldNotBeNull();
        reloaded.DownloadCount.ShouldBe(1);

        var rebatch = await CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bankAccountId, Format = EftFileFormat.Nacha,
            EffectiveDate = FutureDate(2), PaymentEntryIds = new List<Guid> { paymentId }
        });
        rebatch.Succeeded.ShouldBeFalse("付款仍属于原批次，不得重入新批");
        rebatch.Code.ShouldBe(409);
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

    /// <summary>
    /// ★ 档案编辑读档案之后、保存之前生成了一份文件：编辑必须 409，File Creation Number 不得被写回旧值。
    /// </summary>
    /// <remarks>
    /// 序号由 <c>ExecuteUpdate</c> 推进，档案编辑是整行写。不换并发标记的话编辑照常成功并把序号退回去，
    /// 下一份 CPA-005 文件复用一个已用过的序号，被银行按重复文件拒收。
    /// </remarks>
    [Fact]
    public async Task UpdateBankProfile_WhileAFileIsGenerated_Returns409_AndKeepsTheFileCreationNumber()
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
        Result<EftBatchDto>? generated = null;
        _hooks.BeforeProtectWithAad = async () =>
            generated = await InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.GenerateAsync(batch.Data!.Id));

        var updated = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.UpdateAsync(bank, new UpdateBankAccountDto
        {
            Name = "Renamed", Scheme = BankNumberScheme.UsAba, RoutingNumber = "021000021", AccountNumber = "111222333",
            EftOriginatorId = "123456789", EftOriginatorName = "ACME CORP"
        }));

        generated.ShouldNotBeNull("钩子必须真的插进了档案编辑");
        generated.Succeeded.ShouldBeTrue(generated.Message);
        updated.Succeeded.ShouldBeFalse("整行写入不得覆盖在它读档案之后分配出去的序号");
        updated.Code.ShouldBe(409);
        (await ReloadAsync<BankAccount>(bank))!.EftFileCreationNumber.ShouldBe(1);
    }

    /// <summary>已生成文件、从未下载过的批次。</summary>
    private async Task<Guid> GeneratedBatchAsync()
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
        return batch.Data!.Id;
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
    /// <summary>竞态插入点：每个钩子只触发一次，触发前先清空（插入的那个操作自己不会再触发它）。</summary>
    private sealed class RaceHooks
    {
        public Func<Task>? BeforeUnprotect { get; set; }
        public Func<Task>? BeforeLineLoad { get; set; }
        public Func<Task>? BeforeProtectWithAad { get; set; }
    }

    /// <summary>在下载「读到 Generated」之后、「留痕」之前插入另一个操作（下载解密文件正好在两者之间）。</summary>
    private sealed class HookedProtector(IFinanceDataProtector inner, RaceHooks hooks) : IFinanceDataProtector
    {
        public bool IsConfigured => inner.IsConfigured;
        public string Protect(string plaintext) => inner.Protect(plaintext);
        public string Unprotect(string protectedValue, string associatedData) => inner.Unprotect(protectedValue, associatedData);

        /// <summary>档案编辑在「读档案」之后、「保存」之前加密账号。</summary>
        public string Protect(string plaintext, string associatedData)
        {
            var hook = hooks.BeforeProtectWithAad;
            hooks.BeforeProtectWithAad = null;
            if (hook != null)
                Task.Run(hook).GetAwaiter().GetResult();
            return inner.Protect(plaintext, associatedData);
        }

        public string Unprotect(string protectedValue)
        {
            var hook = hooks.BeforeUnprotect;
            hooks.BeforeUnprotect = null;
            // 在线程池上跑完再回来：Unprotect 是同步接口，直接阻塞等待一个 async 钩子会撞上测试框架的同步上下文。
            if (hook != null)
                Task.Run(hook).GetAwaiter().GetResult();
            return inner.Unprotect(protectedValue);
        }
    }

    /// <summary>在作废「读完下载留痕」之后、「提交」之前插入另一个操作（作废在工作单元里先读批次行）。</summary>
    private sealed class HookedLineRepository(FinanceTestDbContext dbContext, IServiceProvider sp, RaceHooks hooks)
        : EFCoreRepository<FinanceTestDbContext, EftBatchLine, Guid>(dbContext, serviceProvider: sp)
    {
        public override async Task<List<EftBatchLine>> ToListAsync(
            System.Linq.Expressions.Expression<Func<EftBatchLine, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            var hook = hooks.BeforeLineLoad;
            hooks.BeforeLineLoad = null;
            if (hook != null)
                await hook();
            return await base.ToListAsync(predicate, cancellationToken);
        }
    }
}
