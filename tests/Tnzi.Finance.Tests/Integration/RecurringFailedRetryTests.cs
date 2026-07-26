using Microsoft.EntityFrameworkCore;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 失败留痕**可重试**（P4-6 第三条不变量）。
/// </summary>
/// <remarks>
/// 排期是无条件往前推的（否则一条坏模板会卡住自己的整条排期），所以失败的那一期
/// 此后再也不会落进"到期"的集合里。生成记录的唯一索引之所以刻意带
/// <c>Status &lt;&gt; Failed</c> 过滤，正是为了让这一期能被重新插入 —— 但那只是把门
/// 留着，还得真有人再来敲。没有这一步，"科目启用回来之后账单自己会补上"就是句空话，
/// 而漏掉的那张发票没有任何人会发现。
/// </remarks>
public class RecurringFailedRetryTests : FinanceIntegrationTestBase
{
    private static DateTime Today() => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

    private async Task<Guid> MonthlyTemplateAsync()
    {
        await SeedCoaAsync();
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = "Retainer Ltd", Currency = "USD" }));
        var revenue = await AccountIdByCodeAsync("4100");

        var created = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(
            s => s.CreateAsync(new CreateRecurringDocumentDto
            {
                Name = "Monthly retainer",
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

    /// <summary>
    /// 把最早那一期改写成"只失败过"，等价于当初它就没成功（科目被停用之类），
    /// 而不必真去破坏一个科目再想办法修回来。
    /// </summary>
    private Task<DateTime> MarkEarliestRunFailedAsync(Guid templateId)
        => InScopeAsync<IRepository<RecurringRun, Guid>, DateTime>(async repo =>
        {
            var runs = await repo.AsQueryable(true)
                .Where(r => r.RecurringDocumentId == templateId && r.Status == RecurringRunStatus.Generated)
                .OrderBy(r => r.PeriodDate)
                .ToListAsync();
            var run = runs[0];

            run.Status = RecurringRunStatus.Failed;
            run.FailReason = "Counter account was disabled.";
            run.DocId = null;
            run.DocNumber = null;
            await repo.UpdateAsync(run);
            await repo.SaveChangesAsync();
            return run.PeriodDate;
        });

    [Fact]
    public async Task Sweep_RevisitsAPeriodThatOnlyEverFailed()
    {
        var templateId = await MonthlyTemplateAsync();

        var first = await SweepAsync();
        first.Succeeded.ShouldBeTrue(first.Message);
        first.Data!.Generated.ShouldBeGreaterThan(1);

        var failedPeriod = await MarkEarliestRunFailedAsync(templateId);

        // 排期此刻已经推过今天，这一期不会再出现在"到期"里 —— 只能靠重试把它捡回来。
        var second = await SweepAsync();
        second.Succeeded.ShouldBeTrue(second.Message);
        second.Data!.Generated.ShouldBe(1);
        second.Data.Runs.Single(r => r.Status == RecurringRunStatus.Generated)
            .PeriodDate.ShouldBe(failedPeriod);
    }

    /// <summary>
    /// 已经办完的期次不该被重试 —— 否则每一轮都会给客户再开一张同期的发票，
    /// 那正是这个模块最不能出的事故。
    /// </summary>
    [Fact]
    public async Task Sweep_DoesNotRevisitPeriodsThatSucceeded()
    {
        await MonthlyTemplateAsync();

        var first = await SweepAsync();
        first.Data!.Generated.ShouldBeGreaterThan(1);

        var second = await SweepAsync();
        second.Succeeded.ShouldBeTrue(second.Message);
        second.Data!.Generated.ShouldBe(0);
    }

    /// <summary>
    /// 重试有上限：一条永远失败的模板不该每轮都往记录表里多写一行。
    /// </summary>
    [Fact]
    public async Task Sweep_StopsRetryingAfterTheAttemptCap()
    {
        var templateId = await MonthlyTemplateAsync();

        var first = await SweepAsync();
        first.Data!.Generated.ShouldBeGreaterThan(1);

        var failedPeriod = await MarkEarliestRunFailedAsync(templateId);

        // 默认上限 3 次：再补两条失败记录就到顶（含最初那一条）。
        await InScopeAsync<IRepository<RecurringRun, Guid>, bool>(async repo =>
        {
            for (var i = 0; i < 2; i++)
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

        var second = await SweepAsync();
        second.Succeeded.ShouldBeTrue(second.Message);
        second.Data!.Generated.ShouldBe(0);
    }
}
