using Microsoft.EntityFrameworkCore;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 没填锚点的月度 / 季度 / 年度模板以起始日那天为锚点，不随短月一路漂到 28 号。
/// </summary>
/// <remarks>
/// 被保护的缺陷：排期实现拿到 null 锚点时只能从上一期推，于是 1 月 31 日起始的月度模板走成
/// 1/31、2/28、3/28、4/28……此后每张发票（与它的到期日）都落在 28 号；年度模板从 2 月 29 日起始则永远停在
/// 2 月 28 日。管理端表单的「On day」是选填项，所以这是默认路径而不是边角情形。
/// </remarks>
public class RecurringAnchorDefaultTests : FinanceIntegrationTestBase
{
    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private async Task<CreateRecurringDocumentDto> TemplateAsync(RecurrenceFrequency frequency, DateTime startDate)
    {
        await SeedCoaAsync();
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = "Month End Ltd", Currency = "USD" }));
        customer.Succeeded.ShouldBeTrue(customer.Message);

        return new CreateRecurringDocumentDto
        {
            Name = "Month-end retainer",
            Kind = RecurringDocKind.Invoice,
            PartyId = customer.Data!.Id,
            Currency = "USD",
            Frequency = frequency,
            StartDate = startDate,
            Lines = [new CreateRecurringLineDto { AccountId = await AccountIdByCodeAsync("4100"), Quantity = 1, UnitPrice = 100m }],
        };
    }

    private Task<Result<RecurrencePreviewDto>> PreviewAsync(CreateRecurringDocumentDto input, int count)
        => InScopeAsync<IRecurringDocumentService, Result<RecurrencePreviewDto>>(s => Task.FromResult(s.PreviewSchedule(input, count)));

    [Fact]
    public async Task Monthly_StartingOnThe31st_ReturnsToMonthEndAfterFebruary()
    {
        var input = await TemplateAsync(RecurrenceFrequency.Monthly, Utc(2027, 1, 31));

        var preview = await PreviewAsync(input, 4);

        preview.Succeeded.ShouldBeTrue(preview.Message);
        preview.Data!.Dates.ShouldBe([Utc(2027, 1, 31), Utc(2027, 2, 28), Utc(2027, 3, 31), Utc(2027, 4, 30)]);
    }

    [Fact]
    public async Task Quarterly_StartingOnThe30th_ReturnsToThe30thAfterFebruary()
    {
        var input = await TemplateAsync(RecurrenceFrequency.Quarterly, Utc(2026, 11, 30));

        var preview = await PreviewAsync(input, 3);

        preview.Data!.Dates.ShouldBe([Utc(2026, 11, 30), Utc(2027, 2, 28), Utc(2027, 5, 30)]);
    }

    [Fact]
    public async Task Yearly_StartingOnFebruary29_ReturnsToThe29thInTheNextLeapYear()
    {
        var input = await TemplateAsync(RecurrenceFrequency.Yearly, Utc(2028, 2, 29));

        var preview = await PreviewAsync(input, 5);

        preview.Data!.Dates.ShouldBe([Utc(2028, 2, 29), Utc(2029, 2, 28), Utc(2030, 2, 28), Utc(2031, 2, 28), Utc(2032, 2, 29)]);
    }

    /// <summary>生成走的是同一条推演：补出来的发票日期落在月末，不是 28 号。</summary>
    [Fact]
    public async Task Generation_ForAMonthlyTemplateStartingOnThe31st_DatesEveryPeriodAtMonthEnd()
    {
        var input = await TemplateAsync(RecurrenceFrequency.Monthly, Utc(2026, 1, 31));
        var created = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.CreateAsync(input));
        created.Succeeded.ShouldBeTrue(created.Message);

        var sweep = await InScopeAsync<IRecurringGeneratorService, Result<RecurringSweepResultDto>>(s => s.RunDueAsync(Utc(2026, 4, 30)));

        sweep.Succeeded.ShouldBeTrue(sweep.Message);
        sweep.Data!.Runs.Where(r => r.Status == RecurringRunStatus.Generated).Select(r => r.PeriodDate).OrderBy(d => d)
            .ShouldBe([Utc(2026, 1, 31), Utc(2026, 2, 28), Utc(2026, 3, 31), Utc(2026, 4, 30)]);
        var template = (await ReloadAsync<RecurringDocument>(created.Data!.Id))!;
        template.NextRunDate.ShouldBe(Utc(2026, 5, 31));
        template.AnchorDay.ShouldBeNull("the stored template keeps 'no anchor'; only the schedule uses the start day");
    }

    /// <summary>已经漂到 28 号的存量模板，下一期自己回到月末，不需要数据修正。</summary>
    [Fact]
    public async Task ADriftedTemplate_ReturnsToMonthEndAtItsNextPeriod()
    {
        var input = await TemplateAsync(RecurrenceFrequency.Monthly, Utc(2026, 1, 31));
        var created = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.CreateAsync(input));
        created.Succeeded.ShouldBeTrue(created.Message);
        await InScopeAsync<IRepository<RecurringDocument, Guid>, bool>(async repo =>
        {
            var template = await repo.AsQueryable(true).SingleAsync(t => t.Id == created.Data!.Id);
            template.NextRunDate = Utc(2026, 3, 28);
            await repo.UpdateAsync(template);
            await repo.SaveChangesAsync();
            return true;
        });

        var preview = await InScopeAsync<IRecurringDocumentService, Result<RecurrencePreviewDto>>(s => s.PreviewAsync(created.Data!.Id, 3));

        preview.Data!.Dates.ShouldBe([Utc(2026, 3, 28), Utc(2026, 4, 30), Utc(2026, 5, 31)]);
    }

    /// <summary>周度不受影响：按天数推进，没有月末夹取。</summary>
    [Fact]
    public async Task Weekly_WithoutAnAnchor_StillStepsBySevenDays()
    {
        var input = await TemplateAsync(RecurrenceFrequency.Weekly, Utc(2027, 1, 31));

        var preview = await PreviewAsync(input, 3);

        preview.Data!.Dates.ShouldBe([Utc(2027, 1, 31), Utc(2027, 2, 7), Utc(2027, 2, 14)]);
    }
}
