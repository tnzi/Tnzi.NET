using Tnzi.Extensions;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 一个<b>违反严格递增</b>的排期实现落在框架上会发生什么。
/// </summary>
/// <remarks>
/// <para>
/// <c>IRecurrenceSchedule</c> 的契约要求 <c>Next(x) &gt; x</c>，并声明「生成器另有单次上限
/// 兜底，但那是护栏不是设计」。这句话在此之前<b>没有任何东西验证</b>，而它错了的后果是
/// 后台服务在一条模板上转不出来 —— 整个宿主的周期性开票停摆且不会有任何报错。
/// </para>
/// <para>
/// ★★ 写这条测试当场抓到一个<b>与坏实现无关</b>的真缺陷：重复期次的插入失败被 catch 住了，
/// 但失败的 <c>Added</c> 实体留在变更跟踪器里，被 <c>AdvanceAsync</c> 的下一次
/// <c>SaveChanges</c> 重放 —— 而那里只接 <c>DbUpdateConcurrencyException</c>，
/// 于是「这一期已经有人做过了」（<b>多实例并发下的正常路径</b>）会把整轮扫描弄死。
/// 详见 <c>RecurringGeneratorService.UndoFailedInsertAsync</c> 的 remarks。
/// </para>
/// <para>
/// 单独成一个测试类而不是在原类里用静态开关切换实现：容器在基类构造时就建好了，
/// 用静态标志控制注册等于让「测试跑在哪个实现上」取决于构造顺序。
/// </para>
/// </remarks>
public class RecurringFrozenScheduleGuardRailTests : FinanceIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddScoped<IRecurrenceSchedule, FrozenSchedule>();
    }

    /// <summary>
    /// 排期永不前进时：扫描仍然收得回来，且同一期<b>只开一张单据</b>。
    /// </summary>
    /// <remarks>
    /// 第二个断言与第一个同样重要：护栏让补齐循环产出 <c>MaxCatchUpPerRun</c> 个
    /// <b>相同</b>的期次日期，若幂等键没兜住，就是给同一期开出二十几张发票 ——
    /// 那是要打电话道歉的事故。
    /// </remarks>
    [Fact]
    public async Task AScheduleThatNeverAdvances_NeitherHangsNorDoubleBills()
    {
        await SeedCoaAsync();
        var start = Today().AddDays(-3);
        var templateId = await CreateTemplateAsync(start);

        var sweep = await InScopeAsync<IRecurringGeneratorService, Result<RecurringSweepResultDto>>(
            s => s.RunDueAsync(Today()));

        // 扫描本身必须成功返回（不是抛异常、也不是转不出来）
        sweep.Succeeded.ShouldBeTrue(sweep.Message);

        var runs = await RunsAsync(templateId);
        runs.Count(r => r.Status == RecurringRunStatus.Generated).ShouldBe(1);
        runs.Select(r => r.PeriodDate.Date).Distinct().Count().ShouldBe(1);
    }

    /// <summary>
    /// ★ 坏排期不得让<b>后面的模板</b>跟着遭殃 —— 这是「一期失败不拖累其它期」的模板级对应。
    /// </summary>
    /// <remarks>
    /// 这条正是上面那个真缺陷的直接症状：失败的 Added 实体重放出的
    /// <c>DbUpdateException</c> 冲出 <c>SweepAsync</c>，于是同一轮里排在后面的模板
    /// 一个都跑不了 —— 而扫描顺序是数据库给的，谁被牵连纯属运气。
    /// </remarks>
    [Fact]
    public async Task AStuckTemplate_DoesNotStopTheRestOfTheSweep()
    {
        await SeedCoaAsync();
        var stuck = await CreateTemplateAsync(Today().AddDays(-3), "Stuck");
        var healthy = await CreateTemplateAsync(Today().AddDays(-1), "Healthy");

        var sweep = await InScopeAsync<IRecurringGeneratorService, Result<RecurringSweepResultDto>>(
            s => s.RunDueAsync(Today()));

        sweep.Succeeded.ShouldBeTrue(sweep.Message);
        (await RunsAsync(stuck)).Count(r => r.Status == RecurringRunStatus.Generated).ShouldBe(1);
        (await RunsAsync(healthy)).Count(r => r.Status == RecurringRunStatus.Generated).ShouldBe(1);
    }

    // ── 夹具 ──────────────────────────────────────────────────────────────────

    private static DateTime Today() => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

    private async Task<Guid> CreateTemplateAsync(DateTime startDate, string name = "Frozen cadence")
    {
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = $"{name} Co", Currency = "USD" }));
        customer.Succeeded.ShouldBeTrue(customer.Message);
        var revenue = await AccountIdByCodeAsync("4100");

        var created = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(
            s => s.CreateAsync(new CreateRecurringDocumentDto
            {
                Name = name,
                Kind = RecurringDocKind.Invoice,
                PartyId = customer.Data!.Id,
                Currency = "USD",
                Frequency = RecurrenceFrequency.Monthly,
                Interval = 1,
                StartDate = startDate,
                DueDays = 30,
                Lines = [new CreateRecurringLineDto { AccountId = revenue, Quantity = 1, UnitPrice = 100m }],
            }));
        created.Succeeded.ShouldBeTrue(created.Message);
        return created.Data!.Id;
    }

    private async Task<List<RecurringRunDto>> RunsAsync(Guid templateId)
    {
        var runs = await InScopeAsync<IRecurringDocumentService, Result<IPagedList<RecurringRunDto>>>(
            s => s.GetRunsAsync(new RecurringRunQueryDto { RecurringDocumentId = templateId, PageSize = 200 }));
        runs.Succeeded.ShouldBeTrue(runs.Message);
        return [.. runs.Data!.Items.OrderBy(r => r.PeriodDate)];
    }

    /// <summary>刻意<b>违反</b>严格递增：永远回到同一天。只用于验证护栏。</summary>
    private sealed class FrozenSchedule : IRecurrenceSchedule
    {
        public DateTime Next(DateTime after, RecurrenceFrequency frequency, int interval, int? anchorDay)
            => after.ToUtcDate();

        public DateTime First(DateTime startDate, RecurrenceFrequency frequency, int? anchorDay)
            => startDate.ToUtcDate();
    }
}
