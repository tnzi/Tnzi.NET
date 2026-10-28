using Microsoft.EntityFrameworkCore;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 资金侧科目（收款存入 / 付款付出 / 费用付出）不得是 A/R、A/P 控制科目：两个方向都拦，草稿与过账都拦。
/// </summary>
/// <remarks>
/// <para>
/// 被保护的缺陷：收付款过账只拒绝**本方向**的控制科目（收款拒 AR、付款拒 AP），费用只校验可过账。
/// 于是一笔客户收款可以「存入」应付账款：借 AP（无往来方）/ 贷 AR（客户）。AR 子账与总账照样一致，
/// AP 控制科目却凭空少了一笔、AP 账龄对不上它；每张凭证自身平衡、试算平衡为零，没有任何报表报警。
/// </para>
/// <para>
/// 存款单那侧对「其它款项」行早有同源守卫，两条合起来才把「从资金单据直接改动控制科目」这条路封死。
/// </para>
/// </remarks>
public class FundsSideControlAccountGuardTests : FinanceIntegrationTestBase
{
    private static readonly DateTime DocDate = new(2026, 4, 10);

    private async Task<Guid> CustomerAsync()
    {
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = $"Customer {Guid.NewGuid():N}" }));
        customer.Succeeded.ShouldBeTrue(customer.Message);
        return customer.Data!.Id;
    }

    private async Task<Guid> VendorAsync()
    {
        var vendor = await InScopeAsync<IVendorService, Result<VendorDto>>(
            s => s.CreateAsync(new CreateVendorDto { Name = $"Vendor {Guid.NewGuid():N}" }));
        vendor.Succeeded.ShouldBeTrue(vendor.Message);
        return vendor.Data!.Id;
    }

    private Task<Result<PaymentEntryDto>> DraftPaymentAsync(PaymentDirection direction, Guid partyId, Guid fundsAccountId)
        => InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = direction,
            PartyType = direction == PaymentDirection.Inbound ? FinancePartyType.Customer : FinancePartyType.Vendor,
            PartyId = partyId,
            DocDate = DocDate,
            Amount = 100m,
            DepositToAccountId = fundsAccountId
        }));

    private async Task<Result<ExpenseDto>> DraftExpenseAsync(Guid paidFromAccountId)
    {
        var expenseAccount = await AccountIdByCodeAsync("5200");
        return await InScopeAsync<IExpenseService, Result<ExpenseDto>>(s => s.CreateDraftAsync(new CreateExpenseDto
        {
            PaidFromAccountId = paidFromAccountId,
            DocDate = DocDate,
            Lines = [new CreateExpenseLineDto { Description = "Supplies", AccountId = expenseAccount, Amount = 40m }]
        }));
    }

    private async Task<int> JournalEntryCountAsync()
    {
        using var scope = ServiceProvider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRepository<JournalEntry, Guid>>().AsNoTracking().CountAsync();
    }

    /// <summary>
    /// 把系统角色从默认科目挪到 <paramref name="toAccountId"/> 上（角色唯一，故先摘再挂）。
    /// 模拟「草稿之后科目表被改了」：过账时必须再判一次，不能只信草稿那一刻的结论。
    /// </summary>
    private async Task MoveRoleAsync(AccountSystemRole role, Guid toAccountId)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<Account, Guid>>();
        var holder = await repo.AsQueryable(true).SingleAsync(a => a.SystemRole == role);
        holder.SystemRole = null;
        await repo.UpdateAsync(holder);
        await repo.SaveChangesAsync();

        var target = await repo.AsQueryable(true).SingleAsync(a => a.Id == toAccountId);
        target.SystemRole = role;
        await repo.UpdateAsync(target);
        await repo.SaveChangesAsync();
    }

    private Task<Guid> PlainAssetAccountAsync(string code)
        => CreateAccountAsync(new CreateAccountDto { Code = code, Name = $"Plain {code}", RootType = AccountRootType.Asset });

    [Theory]
    [InlineData(PaymentDirection.Inbound, "2100")]   // 收款存进 A/P（另一方向的控制科目，旧守卫漏掉的那半）
    [InlineData(PaymentDirection.Inbound, "1200")]   // 收款存进 A/R（本方向，旧守卫只拦这个）
    [InlineData(PaymentDirection.Outbound, "1200")]  // 付款从 A/R 付出
    [InlineData(PaymentDirection.Outbound, "2100")]  // 付款从 A/P 付出
    public async Task PaymentDraft_IntoEitherControlAccount_Rejected400(PaymentDirection direction, string controlCode)
    {
        await SeedCoaAsync();
        var partyId = direction == PaymentDirection.Inbound ? await CustomerAsync() : await VendorAsync();
        var control = await AccountIdByCodeAsync(controlCode);

        var draft = await DraftPaymentAsync(direction, partyId, control);

        draft.Succeeded.ShouldBeFalse();
        draft.Code.ShouldBe(400);
        draft.Message!.ShouldContain(controlCode);
    }

    [Theory]
    [InlineData(PaymentDirection.Inbound, AccountSystemRole.AccountsPayable)]
    [InlineData(PaymentDirection.Outbound, AccountSystemRole.AccountsReceivable)]
    public async Task PaymentPost_AfterTheDraftAccountBecameAControlAccount_Rejected400_AndWritesNothing(PaymentDirection direction, AccountSystemRole role)
    {
        await SeedCoaAsync();
        var partyId = direction == PaymentDirection.Inbound ? await CustomerAsync() : await VendorAsync();
        var funds = await PlainAssetAccountAsync("1190");

        var draft = await DraftPaymentAsync(direction, partyId, funds);
        draft.Succeeded.ShouldBeTrue(draft.Message);

        await MoveRoleAsync(role, funds);
        var before = await JournalEntryCountAsync();

        var posted = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(draft.Data!.Id));

        posted.Succeeded.ShouldBeFalse();
        posted.Code.ShouldBe(400);
        (await JournalEntryCountAsync()).ShouldBe(before);
        var reloaded = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.GetAsync(draft.Data!.Id));
        reloaded.Data!.Status.ShouldBe(FinanceDocumentStatus.Draft);
        reloaded.Data.Number.ShouldBeNull();
    }

    [Fact]
    public async Task PaymentIntoAnOrdinaryFundsAccount_StillPosts()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var bank = await AccountIdByCodeAsync("1120");

        var draft = await DraftPaymentAsync(PaymentDirection.Inbound, customer, bank);
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var posted = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
    }

    [Theory]
    [InlineData("1200")]
    [InlineData("2100")]
    public async Task ExpenseDraft_PaidFromAControlAccount_Rejected400(string controlCode)
    {
        await SeedCoaAsync();
        var control = await AccountIdByCodeAsync(controlCode);

        var draft = await DraftExpenseAsync(control);

        draft.Succeeded.ShouldBeFalse();
        draft.Code.ShouldBe(400);
        draft.Message!.ShouldContain(controlCode);
    }

    [Fact]
    public async Task ExpensePost_AfterThePaidFromAccountBecameAControlAccount_Rejected400_AndWritesNothing()
    {
        await SeedCoaAsync();
        var paidFrom = await PlainAssetAccountAsync("1191");

        var draft = await DraftExpenseAsync(paidFrom);
        draft.Succeeded.ShouldBeTrue(draft.Message);

        await MoveRoleAsync(AccountSystemRole.AccountsPayable, paidFrom);
        var before = await JournalEntryCountAsync();

        var posted = await InScopeAsync<IExpenseService, Result<ExpenseDto>>(s => s.PostAsync(draft.Data!.Id));

        posted.Succeeded.ShouldBeFalse();
        posted.Code.ShouldBe(400);
        (await JournalEntryCountAsync()).ShouldBe(before);
        var reloaded = await InScopeAsync<IExpenseService, Result<ExpenseDto>>(s => s.GetAsync(draft.Data!.Id));
        reloaded.Data!.Status.ShouldBe(FinanceDocumentStatus.Draft);
        reloaded.Data.Number.ShouldBeNull();
    }

    [Fact]
    public async Task ExpensePaidFromAnOrdinaryFundsAccount_StillPosts()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");

        var draft = await DraftExpenseAsync(bank);
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var posted = await InScopeAsync<IExpenseService, Result<ExpenseDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
    }
}
