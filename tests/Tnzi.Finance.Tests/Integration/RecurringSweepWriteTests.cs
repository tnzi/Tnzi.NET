using Microsoft.EntityFrameworkCore;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 扫描只在真有事可做时写模板；写模板撞上并发戳时，过期的那份修改不留在跟踪器里拖累同一轮的其它模板。
/// </summary>
public class RecurringSweepWriteTests : FinanceIntegrationTestBase
{
    private readonly UpdateHook _hook = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<RecurringOptions>(o => o.CatchUpPolicy = RecurringCatchUpPolicy.LatestOnly);
        services.AddSingleton(_hook);
        services.AddScoped<IRepository<RecurringDocument, Guid>>(sp =>
            new HookedRecurringRepository(sp.GetRequiredService<FinanceTestDbContext>(), sp, _hook));
        services.AddScoped<IReadOnlyRepository<RecurringDocument, Guid>>(sp => sp.GetRequiredService<IRepository<RecurringDocument, Guid>>());
    }

    private static DateTime Today() => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

    private async Task<Guid> MonthlyTemplateAsync(string name)
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
                StartDate = Today().AddMonths(-2),
                Lines = [new CreateRecurringLineDto { AccountId = revenue, Quantity = 1, UnitPrice = 100m }],
            }));
        created.Succeeded.ShouldBeTrue(created.Message);
        return created.Data!.Id;
    }

    private Task<Result<RecurringSweepResultDto>> SweepAsync()
        => InScopeAsync<IRecurringGeneratorService, Result<RecurringSweepResultDto>>(s => s.RunDueAsync(Today()));

    private Task<List<RecurringRun>> RunsAsync(Guid templateId)
        => InScopeAsync<IRepository<RecurringRun, Guid>, List<RecurringRun>>(repo =>
            repo.AsQueryable().Where(r => r.RecurringDocumentId == templateId).ToListAsync());

    /// <summary>
    /// 一条失败期次已经重试耗尽的模板，此后每轮都会因为「留有失败行」被捞进扫描。它无事可做，
    /// 就不该被重写：重写会换掉并发戳（开着编辑表单超过一个扫描周期再保存就是 409）、把 LastRunDate
    /// 改成一次什么都没生成的扫描日、把最后修改人抹成后台作用域的 null。
    /// </summary>
    [Fact]
    public async Task ATemplateWithOnlyExhaustedFailures_IsNotRewrittenBySweepsThatHaveNothingToDo()
    {
        await SeedCoaAsync();
        var templateId = await MonthlyTemplateAsync("Exhausted");
        (await SweepAsync()).Succeeded.ShouldBeTrue();

        // 一期只失败过、且已到重试上限（默认 3 次）：它没有可重试的东西了。
        var failedPeriod = Today().AddMonths(-3);
        await InScopeAsync<IRepository<RecurringRun, Guid>, bool>(async repo =>
        {
            for (var i = 0; i < 3; i++)
            {
                await repo.InsertAsync(new RecurringRun
                {
                    RecurringDocumentId = templateId,
                    PeriodDate = failedPeriod,
                    Status = RecurringRunStatus.Failed,
                    FailReason = "Counter account was disabled.",
                });
            }
            await repo.SaveChangesAsync();
            return true;
        });
        var before = (await ReloadAsync<RecurringDocument>(templateId))!;

        var first = await SweepAsync();
        var second = await SweepAsync();

        first.Succeeded.ShouldBeTrue(first.Message);
        second.Succeeded.ShouldBeTrue(second.Message);
        first.Data!.TemplatesDue.ShouldBe(0);
        second.Data!.TemplatesDue.ShouldBe(0);
        var after = (await ReloadAsync<RecurringDocument>(templateId))!;
        after.ConcurrencyStamp.ShouldBe(before.ConcurrencyStamp);
        after.LastRunDate.ShouldBe(before.LastRunDate);
        after.LastModificationTime.ShouldBe(before.LastModificationTime);
    }

    /// <summary>
    /// 扫描推进模板 A 时撞上并发戳（管理员刚改了它，或另一个实例先推进了它）。A 的过期修改若留在跟踪器里，
    /// 同一轮里 B 的下一次提交会把它重放：B 的跳过留痕抛并发异常、B 的排期推进不了、连 B 的失败留痕都写不进去。
    /// </summary>
    [Fact]
    public async Task AConcurrencyConflictOnOneTemplate_DoesNotStopTheNextTemplateFromAdvancing()
    {
        await SeedCoaAsync();
        var first = await MonthlyTemplateAsync("Alpha");
        var second = await MonthlyTemplateAsync("Beta");
        Guid? conflicted = null;
        _hook.BeforeFirstUpdate = async entity =>
        {
            conflicted = entity.Id;
            using var scope = ServiceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<RecurringDocument, Guid>>();
            var other = (await repo.GetAsync(entity.Id))!;
            other.Name = "Renamed meanwhile";
            await repo.UpdateAsync(other);
            await repo.SaveChangesAsync();
        };

        var sweep = await SweepAsync();

        sweep.Succeeded.ShouldBeTrue(sweep.Message);
        conflicted.ShouldNotBeNull("the hook must have run the concurrent write");
        sweep.Data!.Failed.ShouldBe(0);

        var untouched = conflicted == first ? second : first;
        var template = (await ReloadAsync<RecurringDocument>(untouched))!;
        template.NextRunDate.ShouldBeGreaterThan(Today());
        var runs = await RunsAsync(untouched);
        runs.Count(r => r.Status == RecurringRunStatus.Skipped).ShouldBe(2);
        runs.Count(r => r.Status == RecurringRunStatus.Generated).ShouldBe(1);

        // 撞上的那条保留对方的修改。
        (await ReloadAsync<RecurringDocument>(conflicted!.Value))!.Name.ShouldBe("Renamed meanwhile");
    }

    private sealed class UpdateHook
    {
        public Func<RecurringDocument, Task>? BeforeFirstUpdate { get; set; }

        public async Task RunOnceAsync(RecurringDocument entity)
        {
            var action = BeforeFirstUpdate;
            BeforeFirstUpdate = null;
            if (action != null)
                await action(entity);
        }
    }

    private sealed class HookedRecurringRepository(FinanceTestDbContext dbContext, IServiceProvider sp, UpdateHook hook)
        : EFCoreRepository<FinanceTestDbContext, RecurringDocument, Guid>(dbContext, serviceProvider: sp)
    {
        public override async Task UpdateAsync(RecurringDocument entity, CancellationToken cancellationToken = default)
        {
            await hook.RunOnceAsync(entity);
            await base.UpdateAsync(entity, cancellationToken);
        }
    }
}
