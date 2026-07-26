namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 补齐策略（P4-6）：作业停机之后补几期，**由消费方决定**。
/// </summary>
/// <remarks>
/// 三种答案在不同生意里都是对的，所以框架不替他们选；这两个类把"选了会怎样"
/// 变成可执行的断言。跳过的期次一律留痕 —— 跳过是一个决定，不是什么都没发生。
/// </remarks>
public abstract class RecurringCatchUpTestBase : FinanceIntegrationTestBase
{
    protected abstract RecurringCatchUpPolicy Policy { get; }

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<RecurringOptions>(o => o.CatchUpPolicy = Policy);
    }

    protected static DateTime Today() => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

    /// <summary>三个月前起租、每月一期的模板，扫描时已经错过三期以上。</summary>
    protected async Task<Guid> StaleMonthlyTemplateAsync()
    {
        await SeedCoaAsync();
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = "Lapsed Retainer Ltd", Currency = "USD" }));
        var revenue = await AccountIdByCodeAsync("4100");

        var created = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(
            s => s.CreateAsync(new CreateRecurringDocumentDto
            {
                Name = "Lapsed monthly retainer",
                Kind = RecurringDocKind.Invoice,
                PartyId = customer.Data!.Id,
                Currency = "USD",
                Frequency = RecurrenceFrequency.Monthly,
                StartDate = Today().AddMonths(-3),
                Lines = [new CreateRecurringLineDto { AccountId = revenue, Quantity = 1, UnitPrice = 100m }],
            }));
        created.Succeeded.ShouldBeTrue(created.Message);
        return created.Data!.Id;
    }

    protected Task<Result<RecurringSweepResultDto>> SweepAsync()
        => InScopeAsync<IRecurringGeneratorService, Result<RecurringSweepResultDto>>(s => s.RunDueAsync(Today()));
}

/// <summary>
/// LatestOnly：只补最近一期，其余记为跳过。
/// </summary>
public class RecurringCatchUpLatestOnlyTests : RecurringCatchUpTestBase
{
    protected override RecurringCatchUpPolicy Policy => RecurringCatchUpPolicy.LatestOnly;

    [Fact]
    public async Task Sweep_GeneratesOnlyTheLatestPeriod_AndRecordsTheRest()
    {
        var templateId = await StaleMonthlyTemplateAsync();

        var sweep = await SweepAsync();

        sweep.Succeeded.ShouldBeTrue(sweep.Message);
        sweep.Data!.Generated.ShouldBe(1);
        sweep.Data.Skipped.ShouldBeGreaterThanOrEqualTo(2);

        // ★跳过的期次一样留痕：悄悄跳过的那一期，没有人会发现。
        var runs = await InScopeAsync<IRecurringDocumentService, Result<IPagedList<RecurringRunDto>>>(
            s => s.GetRunsAsync(new RecurringRunQueryDto { RecurringDocumentId = templateId, PageSize = 50 }));
        runs.Data!.Items.Count(r => r.Status == RecurringRunStatus.Skipped).ShouldBe(sweep.Data.Skipped);

        // 生成的是最后一期，不是第一期。
        var generated = runs.Data.Items.Single(r => r.Status == RecurringRunStatus.Generated);
        var skippedLatest = runs.Data.Items.Where(r => r.Status == RecurringRunStatus.Skipped).Max(r => r.PeriodDate);
        generated.PeriodDate.ShouldBeGreaterThan(skippedLatest);
    }
}

/// <summary>
/// Skip：一期都不补，排期直接推到下一次。
/// </summary>
public class RecurringCatchUpSkipTests : RecurringCatchUpTestBase
{
    protected override RecurringCatchUpPolicy Policy => RecurringCatchUpPolicy.Skip;

    [Fact]
    public async Task Sweep_GeneratesNothing_ButLeavesATrail()
    {
        var templateId = await StaleMonthlyTemplateAsync();

        var sweep = await SweepAsync();

        sweep.Data!.Generated.ShouldBe(0);
        sweep.Data.Skipped.ShouldBeGreaterThanOrEqualTo(3);

        var invoices = await InScopeAsync<IInvoiceService, Result<IPagedList<InvoiceDto>>>(
            s => s.GetPagedAsync(new InvoiceQueryDto { PageSize = 50 }));
        invoices.Data!.Items.ShouldBeEmpty();

        // 排期照样推进：不补齐不等于永远卡在过去。
        var after = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(
            s => s.GetAsync(templateId));
        after.Data!.NextRunDate.ShouldBeGreaterThan(Today());
        after.Data.OccurrenceCount.ShouldBe(0);
    }
}
