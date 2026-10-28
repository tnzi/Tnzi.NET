using Microsoft.EntityFrameworkCore;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 结束日之内的最后一期失败了，它照样要能被重试。
/// </summary>
/// <remarks>
/// 被保护的缺陷：排期推进在同一轮里看到「下一期越过结束日」就把模板置 Ended，不管这一轮是否失败。
/// 而重试只捡 Active 模板，「立即运行」与恢复也都拒绝 Ended，于是最后一期一失败就再也补不上：
/// 固定期限合同的最后一张发票永久缺失，只留一行 Failed，而那一行是被承诺会重试的。
/// </remarks>
public class RecurringFinalPeriodRetryTests : FinanceIntegrationTestBase
{
    private static DateTime Today() => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

    private Guid _customerId;

    /// <summary>月度模板：P1 = 两个月前、P2 = 一个月前、P3 = 今天 = 结束日。AutoPost 让「客户停用」真的让生成失败。</summary>
    private async Task<Guid> FixedTermTemplateAsync()
    {
        await SeedCoaAsync();
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = "Fixed Term Ltd", Currency = "USD" }));
        customer.Succeeded.ShouldBeTrue(customer.Message);
        _customerId = customer.Data!.Id;
        var revenue = await AccountIdByCodeAsync("4100");

        var created = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(
            s => s.CreateAsync(new CreateRecurringDocumentDto
            {
                Name = "Three-month contract",
                Kind = RecurringDocKind.Invoice,
                PartyId = _customerId,
                Currency = "USD",
                Frequency = RecurrenceFrequency.Monthly,
                StartDate = Today().AddMonths(-2),
                EndDate = Today(),
                AutoPost = true,
                Lines = [new CreateRecurringLineDto { AccountId = revenue, Quantity = 1, UnitPrice = 100m }],
            }));
        created.Succeeded.ShouldBeTrue(created.Message);
        return created.Data!.Id;
    }

    private Task<Result<RecurringSweepResultDto>> SweepAsync(DateTime asOf)
        => InScopeAsync<IRecurringGeneratorService, Result<RecurringSweepResultDto>>(s => s.RunDueAsync(asOf));

    private async Task<RecurringDocumentDto> TemplateStateAsync(Guid id)
        => (await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.GetAsync(id))).Data!;

    private Task<List<RecurringRun>> RunsAsync(Guid templateId)
        => InScopeAsync<IRepository<RecurringRun, Guid>, List<RecurringRun>>(repo =>
            repo.AsQueryable().Where(r => r.RecurringDocumentId == templateId).OrderBy(r => r.PeriodDate).ToListAsync());

    private Task SetCustomerActiveAsync(bool active)
        => InScopeAsync<IRepository<Customer, Guid>, bool>(async repo =>
        {
            var customer = await repo.AsQueryable(true).SingleAsync(c => c.Id == _customerId);
            customer.IsActive = active;
            await repo.UpdateAsync(customer);
            await repo.SaveChangesAsync();
            return true;
        });

    /// <summary>前两期按时生成，最后一期失败：模板保持 Active。</summary>
    private async Task<Guid> FinalPeriodFailsAsync()
    {
        var templateId = await FixedTermTemplateAsync();

        var early = await SweepAsync(Today().AddMonths(-1));
        early.Succeeded.ShouldBeTrue(early.Message);
        early.Data!.Generated.ShouldBe(2);

        await SetCustomerActiveAsync(false);
        var last = await SweepAsync(Today());
        last.Succeeded.ShouldBeTrue(last.Message);
        last.Data!.Failed.ShouldBe(1);
        return templateId;
    }

    [Fact]
    public async Task AFailedFinalPeriod_KeepsTheTemplateActive_AndIsGeneratedOnceTheCauseIsFixed()
    {
        var templateId = await FinalPeriodFailsAsync();

        (await TemplateStateAsync(templateId)).Status.ShouldBe(RecurringStatus.Active,
            "ending the template now would leave the failed final period with no way back");

        await SetCustomerActiveAsync(true);
        var retry = await SweepAsync(Today());
        retry.Succeeded.ShouldBeTrue(retry.Message);
        retry.Data!.Generated.ShouldBe(1);

        var generated = (await RunsAsync(templateId)).Where(r => r.Status == RecurringRunStatus.Generated).ToList();
        generated.Select(r => r.PeriodDate).Distinct().Count().ShouldBe(3, "all three periods of the contract are invoiced");
        (await TemplateStateAsync(templateId)).Status.ShouldBe(RecurringStatus.Ended,
            "with every period inside the end date settled, the template ends");
    }

    [Fact]
    public async Task AFailedFinalPeriod_CanBeRunNow_ByTheOperator()
    {
        var templateId = await FinalPeriodFailsAsync();
        await SetCustomerActiveAsync(true);

        var runNow = await InScopeAsync<IRecurringGeneratorService, Result<RecurringSweepResultDto>>(
            s => s.RunOneAsync(templateId, Today()));

        runNow.Succeeded.ShouldBeTrue(runNow.Message);
        runNow.Data!.Generated.ShouldBe(1);
        (await TemplateStateAsync(templateId)).Status.ShouldBe(RecurringStatus.Ended);
    }

    [Fact]
    public async Task AFinalPeriodThatKeepsFailing_EndsTheTemplateOnceRetriesAreExhausted()
    {
        var templateId = await FinalPeriodFailsAsync();

        // 默认 MaxFailedRetries = 3（含首次）：再失败两次即耗尽。
        (await SweepAsync(Today())).Data!.Failed.ShouldBe(1);
        (await TemplateStateAsync(templateId)).Status.ShouldBe(RecurringStatus.Active);
        (await SweepAsync(Today())).Data!.Failed.ShouldBe(1);

        (await TemplateStateAsync(templateId)).Status.ShouldBe(RecurringStatus.Ended);
        (await RunsAsync(templateId)).Count(r => r.Status == RecurringRunStatus.Failed).ShouldBe(3);
    }

    [Fact]
    public async Task ATemplateTheOperatorEnds_DoesNotRetryItsFailedPeriods()
    {
        var templateId = await FinalPeriodFailsAsync();

        var ended = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.EndAsync(templateId));
        ended.Succeeded.ShouldBeTrue(ended.Message);
        await SetCustomerActiveAsync(true);

        var sweep = await SweepAsync(Today());
        sweep.Succeeded.ShouldBeTrue(sweep.Message);
        sweep.Data!.Generated.ShouldBe(0, "ending a template by hand is the operator saying no more documents");
    }

    [Fact]
    public async Task AFinalPeriodThatSucceeds_EndsTheTemplateInTheSameSweep()
    {
        var templateId = await FixedTermTemplateAsync();

        var sweep = await SweepAsync(Today());
        sweep.Succeeded.ShouldBeTrue(sweep.Message);
        sweep.Data!.Generated.ShouldBe(3);
        (await TemplateStateAsync(templateId)).Status.ShouldBe(RecurringStatus.Ended);
    }
}
