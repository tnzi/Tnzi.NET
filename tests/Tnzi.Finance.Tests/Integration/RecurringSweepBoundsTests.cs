using Microsoft.EntityFrameworkCore;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 扫描的两条边界：<c>asOf</c> 不能落在未来；补齐上限绑定时不丢期次；重试计入次数上限。
/// </summary>
/// <remarks>
/// <para>
/// 被保护的缺陷 ①：<c>run-due</c> / <c>run</c> 的 <c>asOf</c> 没有任何上界。一次
/// <c>POST run-due?asOf=2099-01-01</c>（或运维脚本把年份打错）对每一条 Active 模板生成 24 张日期落在未来的单据
/// （AutoPost 部署直接进总账），把 <c>NextRunDate</c> 推进两年，<c>LastRunDate=2099</c> 还会污染此后的排期重算，
/// 且没有任何撤销路径。<c>asOf</c> 的正当用途是回补过去，未来值没有业务含义。
/// </para>
/// <para>
/// 被保护的缺陷 ②：<c>AdvanceAsync</c> 的推进步数比 <c>ResolvePeriods</c> 的生成步数多一（<c>MaxCatchUpPerRun + 1</c>）。
/// 作业停了 25 期以上时，本轮生成 P1..P24 而排期推到 P26 —— P25 既没有 Generated 也没有 Skipped 行，
/// 重试只捡 Failed 行，于是它永久消失且零症状。
/// </para>
/// <para>
/// 被保护的缺陷 ③：失败期次的重试不计入 <c>MaxOccurrences</c>。一份「只开一期」的订阅，第一期失败、第二期到期时
/// 两张都被生成，客户收到两张发票。
/// </para>
/// </remarks>
public class RecurringSweepBoundsTests : FinanceIntegrationTestBase
{
    private const int CatchUpCap = 3;

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<RecurringOptions>(o => o.MaxCatchUpPerRun = CatchUpCap);
    }

    private static DateTime Today() => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

    private Guid _customerId;

    private async Task<Guid> TemplateAsync(RecurrenceFrequency frequency, DateTime startDate, int? maxOccurrences = null, bool autoPost = false)
    {
        await SeedCoaAsync();
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = "Bounds Ltd", Currency = "USD" }));
        _customerId = customer.Data!.Id;
        var revenue = await AccountIdByCodeAsync("4100");

        var created = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(
            s => s.CreateAsync(new CreateRecurringDocumentDto
            {
                Name = "Bounded template",
                Kind = RecurringDocKind.Invoice,
                PartyId = customer.Data!.Id,
                Currency = "USD",
                Frequency = frequency,
                StartDate = startDate,
                MaxOccurrences = maxOccurrences,
                AutoPost = autoPost,
                Lines = [new CreateRecurringLineDto { AccountId = revenue, Quantity = 1, UnitPrice = 100m }],
            }));
        created.Succeeded.ShouldBeTrue(created.Message);
        return created.Data!.Id;
    }

    private Task<Result<RecurringSweepResultDto>> SweepAsync(DateTime asOf)
        => InScopeAsync<IRecurringGeneratorService, Result<RecurringSweepResultDto>>(s => s.RunDueAsync(asOf));

    private Task<Result<RecurringDocumentDto>> TemplateStateAsync(Guid id)
        => InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.GetAsync(id));

    private Task<List<RecurringRun>> RunsAsync(Guid templateId)
        => InScopeAsync<IRepository<RecurringRun, Guid>, List<RecurringRun>>(repo =>
            repo.AsQueryable().Where(r => r.RecurringDocumentId == templateId).OrderBy(r => r.PeriodDate).ToListAsync());

    /// <summary>
    /// 让生成真的失败，而不是事后改写记录：客户停用后 <c>PostAsync</c> 答「Customer not found or inactive」，
    /// 所以配合 <c>AutoPost = true</c> 的模板使用（草稿本身不查停用）。
    /// </summary>
    private Task SetCustomerActiveAsync(bool active)
        => InScopeAsync<IRepository<Customer, Guid>, bool>(async repo =>
        {
            var customer = await repo.AsQueryable(true).SingleAsync(c => c.Id == _customerId);
            customer.IsActive = active;
            await repo.UpdateAsync(customer);
            await repo.SaveChangesAsync();
            return true;
        });

    private Task MarkFailedAsync(Guid templateId, DateTime period)
        => InScopeAsync<IRepository<RecurringRun, Guid>, bool>(async repo =>
        {
            var run = await repo.AsQueryable(true)
                .SingleAsync(r => r.RecurringDocumentId == templateId && r.PeriodDate == period && r.Status == RecurringRunStatus.Generated);
            run.Status = RecurringRunStatus.Failed;
            run.FailReason = "Counter account was disabled.";
            run.DocId = null;
            run.DocNumber = null;
            await repo.UpdateAsync(run);
            await repo.SaveChangesAsync();
            return true;
        });

    // ── asOf 上界 ───────────────────────────────────────────

    [Fact]
    public async Task RunDue_FutureAsOf_Returns400_AndTouchesNothing()
    {
        var today = Today();
        var templateId = await TemplateAsync(RecurrenceFrequency.Monthly, today);
        var before = (await TemplateStateAsync(templateId)).Data!.NextRunDate;

        var sweep = await SweepAsync(today.AddDays(2));

        sweep.Succeeded.ShouldBeFalse("a future as-of date has no business meaning and cannot be undone");
        sweep.Code.ShouldBe(400);
        (await RunsAsync(templateId)).ShouldBeEmpty();
        (await TemplateStateAsync(templateId)).Data!.NextRunDate.ShouldBe(before);
    }

    /// <summary>时区余量：明天仍然放行（与 <c>LedgerLockService</c> 的封账日守卫同一口径）。</summary>
    [Fact]
    public async Task RunDue_AsOfTomorrow_IsAllowed()
    {
        var today = Today();
        await TemplateAsync(RecurrenceFrequency.Monthly, today);

        var sweep = await SweepAsync(today.AddDays(1));

        sweep.Succeeded.ShouldBeTrue(sweep.Message);
        sweep.Data!.Generated.ShouldBe(1);
    }

    [Fact]
    public async Task RunOne_FutureAsOf_Returns400()
    {
        var today = Today();
        var templateId = await TemplateAsync(RecurrenceFrequency.Monthly, today);

        var run = await InScopeAsync<IRecurringGeneratorService, Result<RecurringSweepResultDto>>(
            s => s.RunOneAsync(templateId, today.AddDays(2)));

        run.Succeeded.ShouldBeFalse();
        run.Code.ShouldBe(400);
        (await RunsAsync(templateId)).ShouldBeEmpty();
    }

    // ── 补齐上限不丢期次 ─────────────────────────────────────

    [Fact]
    public async Task Sweep_MoreThanMaxCatchUpDue_DoesNotSkipAPeriod()
    {
        var today = Today();
        const int periodsBehind = 5; // 上限 3：第一轮 P1..P3，第二轮 P4..P5
        var templateId = await TemplateAsync(RecurrenceFrequency.Daily, today.AddDays(-(periodsBehind - 1)));

        var first = await SweepAsync(today);
        first.Succeeded.ShouldBeTrue(first.Message);
        first.Data!.Generated.ShouldBe(CatchUpCap);

        var second = await SweepAsync(today);
        second.Succeeded.ShouldBeTrue(second.Message);
        second.Data!.Generated.ShouldBe(periodsBehind - CatchUpCap);

        var runs = await RunsAsync(templateId);
        runs.Where(r => r.Status == RecurringRunStatus.Generated).Select(r => r.PeriodDate).Distinct().Count()
            .ShouldBe(periodsBehind, "every due period must end up with a Generated row; a period with no row at all is never revisited");
        (await TemplateStateAsync(templateId)).Data!.NextRunDate.ShouldBe(today.AddDays(1));
    }

    // ── 重试计入次数上限 ─────────────────────────────────────

    [Fact]
    public async Task Retry_DoesNotExceedMaxOccurrences()
    {
        var today = Today();
        var p1 = today.AddMonths(-1);
        var templateId = await TemplateAsync(RecurrenceFrequency.Monthly, p1, maxOccurrences: 1, autoPost: true);

        // 第一轮：p1 因客户停用而失败 —— 留 Failed 行，OccurrenceCount 仍是 0，排期推到 p2。
        await SetCustomerActiveAsync(false);
        var first = await SweepAsync(p1);
        first.Succeeded.ShouldBeTrue(first.Message);
        first.Data!.Failed.ShouldBe(1);
        first.Data.Generated.ShouldBe(0);
        (await TemplateStateAsync(templateId)).Data!.Status.ShouldBe(RecurringStatus.Active);
        await SetCustomerActiveAsync(true);

        // 第二轮：p1 待重试，p2 也到期 —— 一份「只开一期」的订阅只能开出一张。
        var second = await SweepAsync(today);
        second.Succeeded.ShouldBeTrue(second.Message);
        second.Data!.Generated.ShouldBe(1);

        var generated = (await RunsAsync(templateId)).Where(r => r.Status == RecurringRunStatus.Generated).ToList();
        generated.Count.ShouldBe(1);
        generated.Single().PeriodDate.ShouldBe(p1, "the older obligation is honoured first");
        (await TemplateStateAsync(templateId)).Data!.Status.ShouldBe(RecurringStatus.Ended);
    }

    [Fact]
    public async Task Retry_BeyondMovedEndDate_IsNotGenerated()
    {
        var today = Today();
        var templateId = await TemplateAsync(RecurrenceFrequency.Monthly, today.AddMonths(-2));

        var first = await SweepAsync(today);
        first.Data!.Generated.ShouldBe(3);
        await MarkFailedAsync(templateId, today);

        // 操作员把结束日提前到失败那一期之前
        await InScopeAsync<IRepository<RecurringDocument, Guid>, bool>(async repo =>
        {
            var template = await repo.AsQueryable(true).SingleAsync(t => t.Id == templateId);
            template.EndDate = today.AddMonths(-1);
            await repo.UpdateAsync(template);
            await repo.SaveChangesAsync();
            return true;
        });

        var second = await SweepAsync(today);
        second.Succeeded.ShouldBeTrue(second.Message);
        second.Data!.Generated.ShouldBe(0, "a period past the end date must not be generated, retry or not");
    }
}
