using Microsoft.EntityFrameworkCore;
using Tnzi.Extensions;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 一条模板出事不拖累同一轮里的其它模板。
/// </summary>
/// <remarks>
/// 被保护的缺陷：逐模板循环没有任何隔离。排期实现抛异常（消费方的假日表越界、日期越过 9999 年），
/// 整轮扫描随之冲出去：HTTP 路径 500，后台循环里排在它后面的模板每个周期都跳过，只留一行错误日志。
/// 间隔大到乘法溢出时更糟：日期倒着走，每轮补出一批日期越来越早的单据。
/// </remarks>
public class RecurringSweepIsolationTests : FinanceIntegrationTestBase
{
    /// <summary>这个间隔的模板让排期实现抛异常。</summary>
    private const int ThrowingInterval = 7;

    /// <summary>这个间隔的模板让排期实现往回走。</summary>
    private const int BackwardsInterval = 9;

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddScoped<IRecurrenceSchedule, MisbehavingSchedule>();
    }

    private static DateTime Today() => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

    private async Task<Guid> TemplateAsync(string name, int interval, DateTime startDate)
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
                Frequency = RecurrenceFrequency.Daily,
                Interval = interval,
                StartDate = startDate,
                Lines = [new CreateRecurringLineDto { AccountId = revenue, Quantity = 1, UnitPrice = 100m }],
            }));
        created.Succeeded.ShouldBeTrue(created.Message);
        return created.Data!.Id;
    }

    private Task<Result<RecurringSweepResultDto>> SweepAsync()
        => InScopeAsync<IRecurringGeneratorService, Result<RecurringSweepResultDto>>(s => s.RunDueAsync(Today()));

    private Task<List<RecurringRun>> RunsAsync(Guid templateId)
        => InScopeAsync<IRepository<RecurringRun, Guid>, List<RecurringRun>>(repo =>
            repo.AsQueryable().Where(r => r.RecurringDocumentId == templateId).OrderBy(r => r.PeriodDate).ToListAsync());

    [Fact]
    public async Task ATemplateWhoseScheduleThrows_DoesNotStopTheRestOfTheSweep()
    {
        await SeedCoaAsync();
        var broken = await TemplateAsync("Broken", ThrowingInterval, Today());
        var healthy = await TemplateAsync("Healthy", 1, Today());

        var sweep = await SweepAsync();

        sweep.Succeeded.ShouldBeTrue(sweep.Message);
        (await RunsAsync(healthy)).Count(r => r.Status == RecurringRunStatus.Generated).ShouldBe(1);

        // 坏模板的失败看得见：一行 Failed（记在它到期的那一期上）并计入本轮的失败数。
        sweep.Data!.Failed.ShouldBe(1);
        var failed = (await RunsAsync(broken)).ShouldHaveSingleItem();
        failed.Status.ShouldBe(RecurringRunStatus.Failed);
        failed.PeriodDate.ShouldBe(Today());
        failed.FailReason!.ShouldContain("could not be processed");
    }

    [Fact]
    public async Task ATemplateThatKeepsThrowing_DoesNotWriteAFailureRowEverySweep()
    {
        await SeedCoaAsync();
        var broken = await TemplateAsync("Broken", ThrowingInterval, Today());

        for (var i = 0; i < 5; i++)
            (await SweepAsync()).Succeeded.ShouldBeTrue();

        // 默认 MaxFailedRetries = 3：到上限之后只记日志。
        (await RunsAsync(broken)).Count(r => r.Status == RecurringRunStatus.Failed).ShouldBe(3);
    }

    [Fact]
    public async Task RunNow_OnATemplateWhoseScheduleThrows_ReportsTheFailureInsteadOfA500()
    {
        await SeedCoaAsync();
        var broken = await TemplateAsync("Broken", ThrowingInterval, Today());

        var run = await InScopeAsync<IRecurringGeneratorService, Result<RecurringSweepResultDto>>(
            s => s.RunOneAsync(broken, Today()));

        run.Succeeded.ShouldBeTrue(run.Message);
        run.Data!.Failed.ShouldBe(1);
    }

    [Fact]
    public async Task AScheduleThatGoesBackwards_DoesNotGenerateEarlierAndEarlierPeriods()
    {
        await SeedCoaAsync();
        var backwards = await TemplateAsync("Backwards", BackwardsInterval, Today());

        var sweep = await SweepAsync();
        sweep.Succeeded.ShouldBeTrue(sweep.Message);

        var runs = await RunsAsync(backwards);
        runs.Count(r => r.Status == RecurringRunStatus.Generated).ShouldBe(1);
        runs.ShouldAllBe(r => r.PeriodDate == Today());
    }

    /// <summary>间隔 7 抛异常、间隔 9 往回走，其余委托公历实现。</summary>
    private sealed class MisbehavingSchedule : IRecurrenceSchedule
    {
        private readonly CalendarRecurrenceSchedule _calendar = new();

        public DateTime Next(DateTime after, RecurrenceFrequency frequency, int interval, int? anchorDay) => interval switch
        {
            ThrowingInterval => throw new ArgumentOutOfRangeException(nameof(after), "The holiday table has no entries past this date."),
            BackwardsInterval => after.ToUtcDate().AddDays(-1),
            _ => _calendar.Next(after, frequency, interval, anchorDay),
        };

        public DateTime First(DateTime startDate, RecurrenceFrequency frequency, int? anchorDay)
            => _calendar.First(startDate, frequency, anchorDay);
    }
}
