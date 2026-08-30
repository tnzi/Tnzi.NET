namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 生效的账龄切分点必须随桶一起下发（报表 Totals / 行、往来方汇总、对账单）：
/// 呈现端据此生成标签。属性默认值就是 30/60/90，所以这组用例必须跑在
/// <b>自定义</b>切分点上——默认配置下漏掉任何一处 stamping 都测不出来。
/// </summary>
public class AgingBucketDaysFlowTests : FinanceIntegrationTestBase
{
    private static readonly int[] Cuts = [7, 14, 21];

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        // Configure 是追加语义，后注册的委托最后执行 → 覆盖基类里的默认值
        services.Configure<FinanceOptions>(o => o.AgingBucketDays = Cuts);
    }

    private async Task<Guid> CustomerAsync()
    {
        var r = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = "Weekly Terms Ltd", Currency = "USD", PaymentTermsDays = 7 }));
        r.Succeeded.ShouldBeTrue(r.Message);
        return r.Data!.Id;
    }

    private async Task PostedInvoiceAsync(Guid customerId, decimal amount, DateTime docDate, DateTime dueDate)
    {
        var revenue = await AccountIdByCodeAsync("4100");
        var draft = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customerId,
            DocDate = docDate,
            DueDate = dueDate,
            Currency = "USD",
            Lines = [new CreateInvoiceLineDto { AccountId = revenue, Quantity = 1, UnitPrice = amount }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        (await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.PostAsync(draft.Data!.Id)))
            .Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ArAging_CarriesConfiguredCuts_AndBucketsFollowThem()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var today = DateTime.UtcNow.Date;
        // 逾期 10 天：在默认 30/60/90 下落第一桶，在 7/14/21 下落第二桶——
        // 这正是标签必须跟着切分点走的原因。
        await PostedInvoiceAsync(customer, 500m, today.AddDays(-40), today.AddDays(-10));

        var aging = await InScopeAsync<IFinancialReportService, Result<AgingReportDto>>(s => s.GetArAgingAsync(today));

        aging.Succeeded.ShouldBeTrue(aging.Message);
        aging.Data!.Totals.AgingBucketDays.ShouldBe(Cuts);
        var row = aging.Data.Rows.Single(r => r.PartyId == customer);
        row.AgingBucketDays.ShouldBe(Cuts);
        row.Days31To60.ShouldBe(500m); // 历史字段名装的是「第二桶」＝逾期 8-14 天
        row.Days1To30.ShouldBe(0m);
    }

    [Fact]
    public async Task Statement_And_PartySummary_CarryConfiguredCuts()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var today = DateTime.UtcNow.Date;
        await PostedInvoiceAsync(customer, 500m, today.AddDays(-40), today.AddDays(-10));

        var summary = await InScopeAsync<IPartyLedgerService, Result<PartyLedgerSummaryDto>>(
            s => s.GetSummaryAsync(FinancePartyType.Customer, customer));
        summary.Succeeded.ShouldBeTrue(summary.Message);
        summary.Data!.Buckets.AgingBucketDays.ShouldBe(Cuts);

        var statement = await InScopeAsync<ICustomerStatementService, Result<CustomerStatementDto>>(
            s => s.GetAsync(FinancePartyType.Customer, customer, new CustomerStatementQueryDto { To = today }));
        statement.Succeeded.ShouldBeTrue(statement.Message);
        statement.Data!.Buckets.AgingBucketDays.ShouldBe(Cuts);

        // 催收工作台的每行桶同样要带切分点（呈现端的账龄条按它出图例）
        var dunning = await InScopeAsync<ICustomerStatementService, Result<List<DunningCandidateDto>>>(
            s => s.GetDunningCandidatesAsync(FinancePartyType.Customer, today));
        dunning.Succeeded.ShouldBeTrue(dunning.Message);
        dunning.Data!.Single(c => c.PartyId == customer).Buckets.AgingBucketDays.ShouldBe(Cuts);
    }
}
