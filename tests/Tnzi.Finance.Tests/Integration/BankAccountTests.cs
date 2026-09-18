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

    /// <summary>
    /// 出款方账号超过 CPA-005 的 12 位账号字段 → 录入即 400。
    /// </summary>
    /// <remarks>
    /// 收款方账号被截断是一笔付款进了别人的户头；<b>出款方</b>账号被截断是整份报文
    /// 从一个不存在的户头扣款。两侧都不能静默发生，所以两个服务共用同一张方案上限表。
    /// </remarks>
    [Fact]
    public async Task Create_AccountNumberLongerThanTheEftField_Rejects400()
    {
        await SeedCoaAsync();
        var bank = await BankAccountLedgerIdAsync();

        var result = await CreateAsync(new CreateBankAccountDto
        {
            AccountId = bank,
            Name = "Operating CAD",
            Scheme = BankNumberScheme.CaEft,
            InstitutionNumber = "001",
            TransitNumber = "12345",
            AccountNumber = new string('8', 13) // CPA-005 字段宽 12
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>
    /// EFT Originator ID 的录入上限由文件格式的字段宽派生（同账号上限的做法）：
    /// 让操作员在还看得见自己刚敲的那串字符时被拦下，而不是几天后装批生成时才收到报错。
    /// </summary>
    [Theory]
    [InlineData(BankNumberScheme.UsAba)]
    [InlineData(BankNumberScheme.CaEft)]
    public async Task Create_EftOriginatorIdLongerThanTheEftField_Rejects400(BankNumberScheme scheme)
    {
        await SeedCoaAsync();
        var bank = await BankAccountLedgerIdAsync();

        var result = await CreateAsync(new CreateBankAccountDto
        {
            AccountId = bank,
            Name = "Operating",
            Scheme = scheme,
            RoutingNumber = scheme == BankNumberScheme.UsAba ? "021000021" : null,
            InstitutionNumber = scheme == BankNumberScheme.CaEft ? "001" : null,
            TransitNumber = scheme == BankNumberScheme.CaEft ? "12345" : null,
            EftOriginatorId = new string('7', 11), // 两种格式的字段都是 10 位
            EftOriginatorName = "ACME"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("originator id");
    }

    /// <summary>
    /// 非 ASCII 与超长是同一类失效（写进 EFT 文件时不能折叠成另一串合法字符），故也在录入拒绝：
    /// 全角数字录成账号保存 200、装批 200，几天后生成才 400，而报错的人已不是敲字的人。
    /// </summary>
    [Fact]
    public async Task Create_AccountNumberWithNonAsciiCharacters_Rejects400()
    {
        await SeedCoaAsync();
        var bank = await BankAccountLedgerIdAsync();

        var result = await CreateAsync(new CreateBankAccountDto
        {
            AccountId = bank,
            Name = "Operating",
            Scheme = BankNumberScheme.UsAba,
            RoutingNumber = "021000021",
            AccountNumber = "１２３４５６７" // 全角 １２３４５６７，长度合法
        });

        result.Succeeded.ShouldBeFalse("a non-ASCII account number cannot be carried by an EFT file");
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("ASCII");
    }

    [Fact]
    public async Task Create_EftOriginatorIdWithNonAsciiCharacters_Rejects400()
    {
        await SeedCoaAsync();
        var bank = await BankAccountLedgerIdAsync();

        var result = await CreateAsync(new CreateBankAccountDto
        {
            AccountId = bank,
            Name = "Operating",
            Scheme = BankNumberScheme.UsAba,
            RoutingNumber = "021000021",
            EftOriginatorId = "ACMÉ123", // É，长度合法
            EftOriginatorName = "ACME"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("originator id");
        result.Message!.ShouldContain("ASCII");
    }

    [Fact]
    public async Task Update_EftOriginatorIdLongerThanTheEftField_Rejects400()
    {
        await SeedCoaAsync();
        var bank = await BankAccountLedgerIdAsync();
        var created = await CreateAsync(new CreateBankAccountDto { AccountId = bank, Name = "Operating", Scheme = BankNumberScheme.UsAba, RoutingNumber = "021000021", EftOriginatorId = "1234567890" });
        created.Succeeded.ShouldBeTrue(created.Message);

        var updated = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.UpdateAsync(created.Data!.Id, new UpdateBankAccountDto
        {
            Name = "Operating",
            Scheme = BankNumberScheme.UsAba,
            RoutingNumber = "021000021",
            EftOriginatorId = "12345678901"
        }));

        updated.Succeeded.ShouldBeFalse();
        updated.Code.ShouldBe(400);
        (await ReloadAsync<BankAccount>(created.Data!.Id))!.EftOriginatorId.ShouldBe("1234567890");
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
