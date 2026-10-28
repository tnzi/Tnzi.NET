namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 费用模板生成一张真的费用单：供应商、付款科目、行科目与金额（数量 × 单价）都来自模板。
/// </summary>
/// <remarks>
/// 费用模板保存时就要求供应商（与发票 / 账单模板同一条规则），所以生成时供应商恒有值。
/// </remarks>
public class RecurringExpenseGenerationTests : FinanceIntegrationTestBase
{
    [Fact]
    public async Task AnExpenseTemplate_GeneratesAnExpenseForItsVendor()
    {
        await SeedCoaAsync();
        var vendor = await InScopeAsync<IVendorService, Result<VendorDto>>(
            s => s.CreateAsync(new CreateVendorDto { Name = "Bank Fees Inc", Currency = "USD" }));
        vendor.Succeeded.ShouldBeTrue(vendor.Message);
        var bank = await AccountIdByCodeAsync("1120");
        var expenseAccount = await AccountIdByCodeAsync("5200");
        var start = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        var created = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.CreateAsync(new CreateRecurringDocumentDto
        {
            Name = "Monthly bank fee",
            Kind = RecurringDocKind.Expense,
            PartyId = vendor.Data!.Id,
            PaidFromAccountId = bank,
            Currency = "USD",
            Frequency = RecurrenceFrequency.Monthly,
            StartDate = start,
            Lines = [new CreateRecurringLineDto { AccountId = expenseAccount, Description = "Service charge", Quantity = 2, UnitPrice = 12.5m }],
        }));
        created.Succeeded.ShouldBeTrue(created.Message);

        var sweep = await InScopeAsync<IRecurringGeneratorService, Result<RecurringSweepResultDto>>(s => s.RunDueAsync(start));

        sweep.Succeeded.ShouldBeTrue(sweep.Message);
        var run = sweep.Data!.Runs.ShouldHaveSingleItem();
        run.Status.ShouldBe(RecurringRunStatus.Generated, run.FailReason);
        var expense = await InScopeAsync<IExpenseService, Result<ExpenseDto>>(s => s.GetAsync(run.DocId!.Value));
        expense.Succeeded.ShouldBeTrue(expense.Message);
        expense.Data!.VendorId.ShouldBe(vendor.Data!.Id);
        expense.Data.PaidFromAccountId.ShouldBe(bank);
        expense.Data.DocDate.ShouldBe(start);
    }
}
