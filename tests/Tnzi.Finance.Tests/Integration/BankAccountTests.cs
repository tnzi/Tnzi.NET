using Tnzi.Finance.Services.Internal;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// P3 块 0：银行账户档案（加密往返 / 路由号校验 / 唯一 / 资金科目判据 / 支票号）
/// </summary>
public class BankAccountTests : FinanceIntegrationTestBase
{
    private async Task<Guid> BankAccountLedgerIdAsync() => await AccountIdByCodeAsync("1120");

    private Task<Result<BankAccountDto>> CreateAsync(CreateBankAccountDto input)
        => InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.CreateAsync(input));

    [Fact]
    public async Task Create_EncryptsAccountNumber_ReturnsMaskedOnly()
    {
        await SeedCoaAsync();
        var bank = await BankAccountLedgerIdAsync();

        var result = await CreateAsync(new CreateBankAccountDto
        {
            AccountId = bank,
            Name = "Operating USD",
            Scheme = BankNumberScheme.UsAba,
            RoutingNumber = "021000021",
            AccountNumber = "123456789012"
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.AccountNumberMasked.ShouldBe("****9012");

        // 库中密文带版本前缀且非明文
        var entity = await ReloadAsync<BankAccount>(result.Data.Id);
        entity!.AccountNumberEncrypted.ShouldNotBeNull();
        entity.AccountNumberEncrypted!.StartsWith("v2:").ShouldBeTrue(); // AAD 绑定密文
        entity.AccountNumberEncrypted.ShouldNotContain("123456789012");
        entity.AccountNumberMasked.ShouldBe("****9012");
    }

    [Fact]
    public async Task Create_UniquePerLedgerAccount_Rejects409()
    {
        await SeedCoaAsync();
        var bank = await BankAccountLedgerIdAsync();

        (await CreateAsync(new CreateBankAccountDto { AccountId = bank, Name = "First" })).Succeeded.ShouldBeTrue();
        var second = await CreateAsync(new CreateBankAccountDto { AccountId = bank, Name = "Second" });
        second.Succeeded.ShouldBeFalse();
        second.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Create_ValidatesAbaChecksum()
    {
        await SeedCoaAsync();
        var bank = await BankAccountLedgerIdAsync();

        var invalid = await CreateAsync(new CreateBankAccountDto
        {
            AccountId = bank, Name = "Bad", Scheme = BankNumberScheme.UsAba, RoutingNumber = "021000022"
        });
        invalid.Succeeded.ShouldBeFalse();
        invalid.Code.ShouldBe(400);
        invalid.Message!.ShouldContain("checksum");

        var valid = await CreateAsync(new CreateBankAccountDto
        {
            AccountId = bank, Name = "Good", Scheme = BankNumberScheme.UsAba, RoutingNumber = "021000021"
        });
        valid.Succeeded.ShouldBeTrue(valid.Message);
    }

    [Fact]
    public async Task Create_ValidatesCanadianLengths()
    {
        await SeedCoaAsync();
        var bank = await BankAccountLedgerIdAsync();

        var invalid = await CreateAsync(new CreateBankAccountDto
        {
            AccountId = bank, Name = "Bad CA", Scheme = BankNumberScheme.CaEft, InstitutionNumber = "01", TransitNumber = "12345"
        });
        invalid.Succeeded.ShouldBeFalse();
        invalid.Code.ShouldBe(400);

        var valid = await CreateAsync(new CreateBankAccountDto
        {
            AccountId = bank, Name = "Good CA", Scheme = BankNumberScheme.CaEft, InstitutionNumber = "001", TransitNumber = "12345"
        });
        valid.Succeeded.ShouldBeTrue(valid.Message);
    }

    [Fact]
    public async Task Create_WithoutEncryptionKey_Rejects400()
    {
        await SeedCoaAsync();
        var bank = await BankAccountLedgerIdAsync();

        using var scope = ServiceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var unconfigured = new FinanceDataProtector(Microsoft.Extensions.Options.Options.Create(new FinanceEncryptionOptions()));
        unconfigured.IsConfigured.ShouldBeFalse();

        var svc = new BankAccountService(
            sp,
            sp.GetRequiredService<IRepository<BankAccount, Guid>>(),
            sp.GetRequiredService<IReadOnlyRepository<Account, Guid>>(),
            sp.GetRequiredService<IReadOnlyRepository<BankCheck, Guid>>(),
            sp.GetRequiredService<IReadOnlyRepository<EftBatch, Guid>>(),
            sp.GetRequiredService<FinanceDocumentHelper>(),
            unconfigured);

        var result = await svc.CreateAsync(new CreateBankAccountDto { AccountId = bank, Name = "NoKey", AccountNumber = "123456789" });
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("EncryptionKey");

        // 能力面必须先说得出"存不了"，呈现端才能禁用账号字段并解释，
        // 而不是让用户填完账号再吃这个 400
        var capabilities = await svc.GetCapabilitiesAsync();
        capabilities.Succeeded.ShouldBeTrue(capabilities.Message);
        capabilities.Data!.CanStoreAccountNumber.ShouldBeFalse();
    }

    [Fact]
    public async Task GetCapabilities_WithEncryptionKey_AllowsAccountNumber()
    {
        // 测试基类配了 32 字节测试密钥 → 本面能存账号
        var result = await InScopeAsync<IBankAccountService, Result<BankAccountCapabilitiesDto>>(
            s => s.GetCapabilitiesAsync());

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.CanStoreAccountNumber.ShouldBeTrue();
    }

    [Fact]
    public async Task Create_RejectsNonFundsAccount()
    {
        await SeedCoaAsync();
        var ar = await AccountIdByCodeAsync("1200"); // 非资金科目

        var result = await CreateAsync(new CreateBankAccountDto { AccountId = ar, Name = "Not funds" });
        result.Succeeded.ShouldBeFalse();
        result.Message!.ShouldContain("CashEquivalent");
    }

    [Fact]
    public async Task SetNextCheckNumber_Updates()
    {
        await SeedCoaAsync();
        var bank = await BankAccountLedgerIdAsync();
        var created = await CreateAsync(new CreateBankAccountDto { AccountId = bank, Name = "Checks", NextCheckNumber = 100 });
        created.Data!.NextCheckNumber.ShouldBe(100);

        var updated = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(
            s => s.SetNextCheckNumberAsync(created.Data.Id, new SetNextCheckNumberDto { NextCheckNumber = 5000 }));
        updated.Succeeded.ShouldBeTrue(updated.Message);
        updated.Data!.NextCheckNumber.ShouldBe(5000);
    }
}
