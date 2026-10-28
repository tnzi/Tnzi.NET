using Microsoft.EntityFrameworkCore;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 科目币种：空白等于「不限币种」，存成 null 而不是空串。
/// </summary>
/// <remarks>
/// 空串既不是 null（过账引擎按「限定币种」处理，该科目从此只收本位币），也不是本位币
/// （期末重估把它当外币科目选中，随后按币种 "" 解析汇率当场抛异常，整次重估 500）。
/// 管理端表单自己会把空白归一成 null，所以只有 API / 导入调用方会撞上。
/// </remarks>
public class AccountCurrencyNormalizationTests : FinanceIntegrationTestBase
{
    private async Task<Guid> CreateBankAsync(string code, string? currency)
        => await CreateAccountAsync(new CreateAccountDto
        {
            Code = code,
            Name = $"Bank {code}",
            RootType = AccountRootType.Asset,
            Currency = currency,
            ParentId = await AccountIdByCodeAsync("1100"),
            SubType = "Bank",
            CashFlowActivity = CashFlowActivity.CashEquivalent,
        });

    [Fact]
    public async Task Create_WithAWhitespaceCurrency_StoresNoCurrency()
    {
        await SeedCoaAsync();

        var id = await CreateBankAsync("1131", "   ");

        (await ReloadAsync<Account>(id))!.Currency.ShouldBeNull();
    }

    [Fact]
    public async Task Update_WithAWhitespaceCurrency_StoresNoCurrency()
    {
        await SeedCoaAsync();
        var id = await CreateBankAsync("1132", "EUR");
        var account = (await ReloadAsync<Account>(id))!;

        var updated = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.UpdateAsync(id, new UpdateAccountDto
        {
            Code = account.Code,
            Name = account.Name,
            ParentId = account.ParentId,
            SubType = account.SubType,
            Currency = " ",
            CashFlowActivity = account.CashFlowActivity,
            IsActive = true,
        }));

        updated.Succeeded.ShouldBeTrue(updated.Message);
        (await ReloadAsync<Account>(id))!.Currency.ShouldBeNull();
    }

    /// <summary>
    /// 一条空串币种的存量科目（规范化之前写进来的）带着余额：重估不再把它当外币科目选中然后整次 500。
    /// </summary>
    [Fact]
    public async Task Revaluation_WithALegacyEmptyCurrencyAccount_DoesNotFail()
    {
        await SeedCoaAsync();
        var id = await CreateBankAsync("1133", null);
        await InScopeAsync<IRepository<Account, Guid>, int>(async repo =>
        {
            var account = await repo.AsQueryable(true).FirstAsync(a => a.Id == id);
            account.Currency = "";
            await repo.UpdateAsync(account);
            await repo.SaveChangesAsync();
            return 0;
        });
        var posted = await PostLedgerAsync(Posting(new DateTime(2026, 1, 15), "USD", null, "legacy-in",
            new LedgerPostingLine { AccountId = id, Debit = 100m },
            new LedgerPostingLine { AccountCode = "3100", Credit = 100m }));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var preview = await InScopeAsync<IRevaluationService, Result<RevaluationPreviewDto>>(
            s => s.PreviewAsync(new RunRevaluationDto { AsOf = new DateTime(2026, 3, 31) }));

        preview.Succeeded.ShouldBeTrue(preview.Message);
        preview.Data!.Rows.ShouldNotContain(r => r.AccountId == id);
    }
}
