using Microsoft.EntityFrameworkCore;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// P3 块 0：往来方银行账户（remit-to：加密 / 默认唯一维护 / 按往来方查询）
/// </summary>
public class PartyBankAccountTests : FinanceIntegrationTestBase
{
    private readonly Guid _party = Guid.NewGuid();

    private Task<Result<PartyBankAccountDto>> CreateAsync(SavePartyBankAccountDto input)
        => InScopeAsync<IPartyBankAccountService, Result<PartyBankAccountDto>>(s => s.CreateAsync(input));

    private SavePartyBankAccountDto Input(bool isDefault, string? accountNumber = null, string? label = null) => new()
    {
        PartyType = FinancePartyType.Vendor,
        PartyId = _party,
        Label = label,
        Scheme = BankNumberScheme.UsAba,
        RoutingNumber = "021000021",
        AccountNumber = accountNumber,
        IsDefault = isDefault
    };

    [Fact]
    public async Task Create_EncryptsAccountNumber_ReturnsMaskedOnly()
    {
        await SeedCoaAsync();
        var result = await CreateAsync(Input(false, "987654321000"));
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.AccountNumberMasked.ShouldBe("****1000");

        var entity = await ReloadAsync<PartyBankAccount>(result.Data.Id);
        entity!.AccountNumberEncrypted!.StartsWith("v2:").ShouldBeTrue(); // AAD 绑定密文
        entity.AccountNumberEncrypted.ShouldNotContain("987654321000");
    }

    [Fact]
    public async Task CreateWithDefault_ClearsPriorDefault()
    {
        await SeedCoaAsync();
        var first = await CreateAsync(Input(isDefault: true, label: "A"));
        first.Succeeded.ShouldBeTrue(first.Message);

        var second = await CreateAsync(Input(isDefault: true, label: "B"));
        second.Succeeded.ShouldBeTrue(second.Message);

        var reloadFirst = await ReloadAsync<PartyBankAccount>(first.Data!.Id);
        reloadFirst!.IsDefault.ShouldBeFalse();
        var reloadSecond = await ReloadAsync<PartyBankAccount>(second.Data!.Id);
        reloadSecond!.IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task SetDefault_ClearsOthers()
    {
        await SeedCoaAsync();
        var a = await CreateAsync(Input(isDefault: true, label: "A"));
        var b = await CreateAsync(Input(isDefault: false, label: "B"));

        var setB = await InScopeAsync<IPartyBankAccountService, Result<PartyBankAccountDto>>(s => s.SetDefaultAsync(b.Data!.Id));
        setB.Succeeded.ShouldBeTrue(setB.Message);

        (await ReloadAsync<PartyBankAccount>(a.Data!.Id))!.IsDefault.ShouldBeFalse();
        (await ReloadAsync<PartyBankAccount>(b.Data!.Id))!.IsDefault.ShouldBeTrue();
    }

    /// <summary>
    /// 回归（B8）：绕过服务的 clear 逻辑直接插两行 IsDefault=true → 过滤唯一索引兜底拒绝。
    /// 这是并发 SetDefault/Create 竞态的真墙（服务层 clear-then-set 只挡单线程，DB 索引挡跨事务）。
    /// </summary>
    [Fact]
    public async Task DuplicateDefault_RejectedByDbIndex()
    {
        await SeedCoaAsync();
        var repo = ServiceProvider.GetRequiredService<IRepository<PartyBankAccount, Guid>>();

        var a = new PartyBankAccount { PartyType = FinancePartyType.Vendor, PartyId = _party, IsDefault = true, Scheme = BankNumberScheme.UsAba };
        await repo.InsertAsync(a);
        await repo.SaveChangesAsync();

        var b = new PartyBankAccount { PartyType = FinancePartyType.Vendor, PartyId = _party, IsDefault = true, Scheme = BankNumberScheme.UsAba };
        await Should.ThrowAsync<DbUpdateException>(async () =>
        {
            await repo.InsertAsync(b);
            await repo.SaveChangesAsync();
        });
    }

    /// <summary>
    /// 账号超过该方案下 EFT 文件的定宽账号字段 → 录入即 400。
    /// </summary>
    /// <remarks>
    /// 此前录一个 20 位账号，保存 / 装批 / 生成三步全程 200，而文件里只有前 17 位 ——
    /// 银行按一个不存在、或<b>属于别人</b>的账号处理。在录入拒绝，操作员还看得见
    /// 自己刚敲的那串数字；留到生成阶段才拒，报错与他当时在做的事已经无关了。
    /// </remarks>
    [Fact]
    public async Task Create_AccountNumberLongerThanTheEftField_Rejects400()
    {
        await SeedCoaAsync();

        var result = await CreateAsync(Input(false, new string('9', 18))); // UsAba 字段宽 17

        result.Succeeded.ShouldBeFalse("超长账号会在写进 EFT 文件时被截断成另一个账号");
        result.Code.ShouldBe(400);

        // 拒绝路径零副作用：没有半条记录留下
        (await InScopeAsync<IPartyBankAccountService, Result<List<PartyBankAccountDto>>>(
            s => s.GetByPartyAsync(FinancePartyType.Vendor, _party))).Data!.Count.ShouldBe(0);
    }

    /// <summary>加拿大方案的上限更严（CPA-005 账号字段 12 位）—— 上限跟着方案走，不是一个全局常数。</summary>
    [Fact]
    public async Task Create_CanadianAccountNumberUsesTheTighterLimit()
    {
        await SeedCoaAsync();

        var input = Input(false, new string('4', 13));
        input.Scheme = BankNumberScheme.CaEft;
        input.RoutingNumber = null;
        input.InstitutionNumber = "001";
        input.TransitNumber = "12345";

        var tooLong = await CreateAsync(input);
        tooLong.Succeeded.ShouldBeFalse("CPA-005 的账号字段只有 12 位");
        tooLong.Code.ShouldBe(400);

        // 同一串数字在美国方案下是合法的 —— 证明拒绝来自方案上限而不是别的校验
        var usInput = Input(false, new string('4', 13));
        (await CreateAsync(usInput)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task GetByParty_ReturnsDefaultFirst()
    {
        await SeedCoaAsync();
        await CreateAsync(Input(isDefault: false, label: "A"));
        await CreateAsync(Input(isDefault: true, label: "B"));

        var list = await InScopeAsync<IPartyBankAccountService, Result<List<PartyBankAccountDto>>>(
            s => s.GetByPartyAsync(FinancePartyType.Vendor, _party));
        list.Succeeded.ShouldBeTrue(list.Message);
        list.Data!.Count.ShouldBe(2);
        list.Data[0].Label.ShouldBe("B");
        list.Data[0].IsDefault.ShouldBeTrue();
    }
}
