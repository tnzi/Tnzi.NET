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
        return vendor.Data.Id;
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
        var voided = await InScopeAsync<IEftService, Result<EftBatchDto>>(s => s.VoidBatchAsync(batch1.Data.Id, new VoidEftBatchDto { Reason = "Rebuild" }));
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
    }
}
