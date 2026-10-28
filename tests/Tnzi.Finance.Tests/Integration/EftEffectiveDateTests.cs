namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// EFT 生效日（结算日）：按注入的时钟判断，生成时再判一次，且不因操作员所在时区早于 UTC 而拒掉他的「今天」。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>草稿批次可以搁置任意天：只在装批时判一次，几天后生成出来的文件带着一个已经过去的生效日，
/// 银行要么拒收、要么按它自己的规则改日结算。</item>
/// <item>管理端发的是浏览器本地的日历日，而服务端拿 UTC 日期比：北美晚上八点之后 UTC 已是明天，
/// 表单默认值（今天）被当成「过去」拒掉。只拒绝「在任何时区都已经过去」的日期。</item>
/// </list>
/// </remarks>
public class EftEffectiveDateTests : FinanceIntegrationTestBase
{
    private readonly MovableTimeProvider _clock = new(new DateTimeOffset(2031, 6, 10, 15, 0, 0, TimeSpan.Zero));

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        // 基类注册的是 TimeProvider.System；后注册者胜出
        services.AddSingleton<TimeProvider>(_clock);
    }

    private async Task<(Guid Bank, Guid Payment)> ScenarioAsync()
    {
        await SeedCoaAsync();
        var ledger = await AccountIdByCodeAsync("1120");
        var bank = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.CreateAsync(new CreateBankAccountDto
        {
            AccountId = ledger, Name = "Operating", Scheme = BankNumberScheme.UsAba, RoutingNumber = "021000021",
            AccountNumber = "111222333", EftOriginatorId = "123456789", EftOriginatorName = "ACME CORP"
        }));
        bank.Succeeded.ShouldBeTrue(bank.Message);

        var vendor = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = "Acme Supplies" }));
        vendor.Succeeded.ShouldBeTrue(vendor.Message);
        var party = await InScopeAsync<IPartyBankAccountService, Result<PartyBankAccountDto>>(s => s.CreateAsync(new SavePartyBankAccountDto
        {
            PartyType = FinancePartyType.Vendor, PartyId = vendor.Data!.Id, Scheme = BankNumberScheme.UsAba,
            RoutingNumber = "011401533", AccountNumber = "1234567", IsDefault = true
        }));
        party.Succeeded.ShouldBeTrue(party.Message);

        var draft = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Outbound, PartyType = FinancePartyType.Vendor, PartyId = vendor.Data!.Id,
            DocDate = new DateTime(2031, 6, 1), Currency = "USD", Amount = 100m, DepositToAccountId = ledger,
            PaymentMethod = PaymentMethods.BankTransfer
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var posted = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        return (bank.Data!.Id, posted.Data!.Id);
    }

    private Task<Result<EftBatchDto>> CreateBatchAsync(Guid bank, Guid payment, DateTime effectiveDate)
        => InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.CreateBatchAsync(new CreateEftBatchDto
        {
            BankAccountId = bank, Format = EftFileFormat.Nacha, EffectiveDate = effectiveDate,
            PaymentEntryIds = new List<Guid> { payment }
        }));

    /// <summary>★ 草稿搁置到生效日过去之后再生成：409，批次留在草稿、不分配任何号。</summary>
    [Fact]
    public async Task Generate_AfterTheEffectiveDateHasPassed_Returns409_AndAllocatesNothing()
    {
        var (bank, payment) = await ScenarioAsync();
        var batch = await CreateBatchAsync(bank, payment, new DateTime(2031, 6, 12));
        batch.Succeeded.ShouldBeTrue(batch.Message);

        _clock.Now = new DateTimeOffset(2031, 6, 14, 15, 0, 0, TimeSpan.Zero);
        var generated = await InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.GenerateAsync(batch.Data!.Id));

        generated.Succeeded.ShouldBeFalse();
        generated.Code.ShouldBe(409);
        generated.Message!.ShouldContain("Void the batch");
        var reloaded = (await ReloadAsync<EftBatch>(batch.Data!.Id))!;
        reloaded.Status.ShouldBe(EftBatchStatus.Draft);
        reloaded.Number.ShouldBeNull();
        (await ReloadAsync<BankAccount>(bank))!.EftFileCreationNumber.ShouldBe(0);
    }

    /// <summary>对照：生效日还没过，生成照常。</summary>
    [Fact]
    public async Task Generate_BeforeTheEffectiveDate_Succeeds()
    {
        var (bank, payment) = await ScenarioAsync();
        var batch = await CreateBatchAsync(bank, payment, new DateTime(2031, 6, 12));
        batch.Succeeded.ShouldBeTrue(batch.Message);

        _clock.Now = new DateTimeOffset(2031, 6, 11, 15, 0, 0, TimeSpan.Zero);
        var generated = await InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.GenerateAsync(batch.Data!.Id));

        generated.Succeeded.ShouldBeTrue(generated.Message);
    }

    /// <summary>装批时同样按注入的时钟判断「过去」（不是机器的真实时钟）。</summary>
    [Fact]
    public async Task CreateBatch_WithADatePastByTheInjectedClock_Rejects400()
    {
        var (bank, payment) = await ScenarioAsync();

        var batch = await CreateBatchAsync(bank, payment, new DateTime(2031, 6, 8));

        batch.Succeeded.ShouldBeFalse();
        batch.Code.ShouldBe(400);
    }

    /// <summary>
    /// UTC 已是 6 月 15 日 01:30，多伦多（EDT）还是 6 月 14 日晚上 9:30：操作员选的「今天」不是过去。
    /// </summary>
    [Fact]
    public async Task CreateBatch_WithTodayInATimeZoneBehindUtc_IsAccepted()
    {
        var (bank, payment) = await ScenarioAsync();
        _clock.Now = new DateTimeOffset(2031, 6, 15, 1, 30, 0, TimeSpan.Zero);

        var batch = await CreateBatchAsync(bank, payment, new DateTime(2031, 6, 14));

        batch.Succeeded.ShouldBeTrue(batch.Message);
    }

    private sealed class MovableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
