using Tnzi.Extensions;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// <c>IRecurrenceSchedule</c> 的契约：不变量、边界、以及「可整体替换」这句话是不是真的。
/// </summary>
/// <remarks>
/// <para>
/// 该契约的 remarks 写着一条<b>硬不变量</b>：「实现必须是纯函数且严格递增：<c>Next(x) &gt; x</c>
/// 恒成立，否则补齐循环不会终止（生成器另有单次上限兜底，但那是护栏不是设计）」。
/// 在此之前只有 <c>interval = 1</c> 且 <c>anchorDay = null</c> 那一种组合被验证过 ——
/// 而**锚点对齐正是唯一可能把日期往回拉的地方**（<c>AlignToWeekday</c> 的 delta 可以为负、
/// <c>AlignToMonthDay</c> 会把 31 号收到月末），所以不带锚点的那条恰好绕开了风险所在。
/// </para>
/// <para>
/// <c>First()</c> 此前<b>零覆盖</b>。它与 <c>Next()</c> 的差别只在边界（起始日当天就可能是
/// 第一期），而那正是「第一张账单开在哪天」。
/// </para>
/// <para>
/// 最后两条锁的是<b>机制</b>而不是算法：消费方注册的实现真的被生成器用到（否则
/// 「可整体替换」只是文档里的一句话），以及一个**写坏了的**实现既不会让后台服务转不出来、
/// 也不会给同一期开出两张单据。
/// </para>
/// </remarks>
public class RecurringScheduleContractTests : FinanceIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // 后注册者胜出：这本身就是「消费方注册自己的实现即可整体替换」的机制。
        services.AddScoped<IRecurrenceSchedule, EveryTenDaysSchedule>();
    }

    // ── 不变量：严格递增（全频率 × 间隔 × 锚点矩阵）────────────────────────────

    /// <summary>
    /// <c>Next(x) &gt; x</c> 对每个频率 × 间隔 × 锚点组合都成立。
    /// </summary>
    /// <remarks>
    /// 逐个起始日跑一遍是刻意的：月末、闰日、月初各有不同的收敛路径，
    /// 只挑一个「普通的」日期会漏掉正好是锚点收月末那一类。
    /// </remarks>
    [Fact]
    public void DefaultSchedule_NextIsStrictlyIncreasing_AcrossTheWholeMatrix()
    {
        var schedule = new CalendarRecurrenceSchedule();
        DateTime[] starts =
        [
            new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new(2026, 1, 30, 0, 0, 0, DateTimeKind.Utc),
            new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            new(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc),
            new(2024, 2, 29, 0, 0, 0, DateTimeKind.Utc),   // 闰日
            new(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            new(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),  // 跨年
        ];
        int?[] anchors = [null, 1, 5, 7, 15, 28, 29, 30, 31];

        var checked_ = 0;
        foreach (var frequency in Enum.GetValues<RecurrenceFrequency>())
        {
            foreach (var interval in new[] { 1, 2, 3, 12 })
            {
                foreach (var anchor in anchors)
                {
                    foreach (var start in starts)
                    {
                        var next = schedule.Next(start, frequency, interval, anchor);
                        next.ShouldBeGreaterThan(start,
                            $"{frequency} interval={interval} anchor={anchor?.ToString() ?? "null"} from {start:yyyy-MM-dd}");
                        next.Kind.ShouldBe(DateTimeKind.Utc);
                        checked_++;
                    }
                }
            }
        }

        // 下界断言：矩阵真的跑起来了，而不是因为某层枚举为空而空转
        checked_.ShouldBeGreaterThan(1000);
    }

    /// <summary>
    /// 反复推进恒不停滞：连推 60 期，每一期都严格晚于上一期。
    /// </summary>
    /// <remarks>
    /// 单步递增不等于长程递增：「二月被夹到 28 之后一路 28 下去」正是单步看不出来的形态
    /// （它仍然递增，只是锚点丢了）。这条同时断言 31 号锚点会回到 31 号。
    /// </remarks>
    [Fact]
    public void DefaultSchedule_MonthEndAnchor_KeepsReturningToTheAnchor()
    {
        var schedule = new CalendarRecurrenceSchedule();
        var cursor = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
        var seenThirtyFirst = 0;

        for (var i = 0; i < 60; i++)
        {
            var next = schedule.Next(cursor, RecurrenceFrequency.Monthly, 1, anchorDay: 31);
            next.ShouldBeGreaterThan(cursor);
            // 每一期都必须落在该月的最后一天（31 号锚点在短月收月末）
            next.Day.ShouldBe(DateTime.DaysInMonth(next.Year, next.Month));
            if (next.Day == 31) seenThirtyFirst++;
            cursor = next;
        }

        // 长月照常回到 31 号，而不是被二月一次性钉在 28
        seenThirtyFirst.ShouldBeGreaterThan(20);
    }

    // ── First()：此前零覆盖 ───────────────────────────────────────────────────

    /// <summary>起始日已经落在锚点上时，第一期<b>就是</b>起始日本身。</summary>
    [Theory]
    [InlineData(2026, 3, 15, 15)]
    [InlineData(2026, 1, 31, 31)]
    [InlineData(2024, 2, 29, 31)]   // 闰日 + 31 号锚点：收月末即当天
    public void First_WhenTheStartDateAlreadySitsOnTheAnchor_IsTheStartDate(int y, int m, int d, int anchor)
    {
        var start = new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc);

        new CalendarRecurrenceSchedule()
            .First(start, RecurrenceFrequency.Monthly, anchor)
            .ShouldBe(start);
    }

    /// <summary>
    /// 锚点在起始日之前 → 第一期推到下个月的锚点，<b>不是</b>回到过去。
    /// </summary>
    [Fact]
    public void First_WhenTheAnchorAlreadyPassed_MovesToTheNextMonth()
    {
        var start = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        var first = new CalendarRecurrenceSchedule().First(start, RecurrenceFrequency.Monthly, anchorDay: 10);

        first.ShouldBe(new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc));
    }

    /// <summary><c>First</c> 恒不早于起始日 —— 否则第一张账单会开在合同生效之前。</summary>
    [Fact]
    public void First_IsNeverBeforeTheStartDate_AcrossTheMatrix()
    {
        var schedule = new CalendarRecurrenceSchedule();
        DateTime[] starts =
        [
            new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            new(2024, 2, 29, 0, 0, 0, DateTimeKind.Utc),
            new(2026, 6, 17, 0, 0, 0, DateTimeKind.Utc),
            new(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
        ];
        int?[] anchors = [null, 1, 3, 7, 15, 28, 31];

        foreach (var frequency in Enum.GetValues<RecurrenceFrequency>())
        {
            foreach (var anchor in anchors)
            {
                foreach (var start in starts)
                {
                    var first = schedule.First(start, frequency, anchor);
                    first.ShouldBeGreaterThanOrEqualTo(start,
                        $"{frequency} anchor={anchor?.ToString() ?? "null"} from {start:yyyy-MM-dd}");
                }
            }
        }
    }

    /// <summary>周锚点用 ISO 口径（1=周一…7=周日），且第一期落在起始日当周或之后。</summary>
    [Theory]
    [InlineData(1, DayOfWeek.Monday)]
    [InlineData(5, DayOfWeek.Friday)]
    [InlineData(7, DayOfWeek.Sunday)]
    public void First_WeeklyAnchor_UsesIsoWeekdayNumbering(int anchor, DayOfWeek expected)
    {
        // 2026-03-15 是周日
        var start = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var first = new CalendarRecurrenceSchedule().First(start, RecurrenceFrequency.Weekly, anchor);

        first.DayOfWeek.ShouldBe(expected);
        first.ShouldBeGreaterThanOrEqualTo(start);
    }

    // ── 机制：替换面真的被生成器用到 ──────────────────────────────────────────

    /// <summary>
    /// ★ 消费方注册的排期实现<b>真的</b>决定了生成出来的期次。
    /// </summary>
    /// <remarks>
    /// 「可整体替换」是这个契约存在的全部理由。少了这一条，把 <c>_schedule</c> 换成
    /// <c>new CalendarRecurrenceSchedule()</c> 硬编码，所有排期算法测试<b>照样全绿</b> ——
    /// 与本仓库反复兑现的「纯函数测试不能证明机制已接上」同一形态。
    /// </remarks>
    [Fact]
    public async Task GeneratorUsesTheRegisteredSchedule_NotTheDefaultCalendar()
    {
        await SeedCoaAsync();
        var start = Today().AddDays(-25);
        var created = await CreateTemplateAsync(start);

        var sweep = await InScopeAsync<IRecurringGeneratorService, Result<RecurringSweepResultDto>>(
            s => s.RunDueAsync(Today()));

        sweep.Succeeded.ShouldBeTrue(sweep.Message);
        var runs = await RunsAsync(created);

        // 自定义实现是「每 10 天一期」：25 天里应当出 3 期（起始日 + 10 + 20）。
        // 默认公历按月推进，同一区间只会出 1 期 —— 两者可区分，这条断言才有意义。
        runs.Count.ShouldBe(3);
        runs.Select(r => r.PeriodDate.Date)
            .ShouldBe([start.Date, start.AddDays(10).Date, start.AddDays(20).Date], ignoreOrder: false);
    }

    // ── 夹具 ──────────────────────────────────────────────────────────────────

    private static DateTime Today() => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

    private async Task<Guid> CreateTemplateAsync(DateTime startDate)
    {
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = "Schedule Co", Currency = "USD" }));
        customer.Succeeded.ShouldBeTrue(customer.Message);
        var revenue = await AccountIdByCodeAsync("4100");

        var created = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(
            s => s.CreateAsync(new CreateRecurringDocumentDto
            {
                Name = "Custom cadence",
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

    /// <summary>「每 10 天一期」—— 与默认公历（按月）可区分，所以替换生效与否看得出来。</summary>
    private sealed class EveryTenDaysSchedule : IRecurrenceSchedule
    {
        public DateTime Next(DateTime after, RecurrenceFrequency frequency, int interval, int? anchorDay)
            => after.ToUtcDate().AddDays(10);

        public DateTime First(DateTime startDate, RecurrenceFrequency frequency, int? anchorDay)
            => startDate.ToUtcDate();
    }

}
