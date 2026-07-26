namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 周期性单据（P4-6）：排期、幂等、补齐策略、生成即真单据。
/// </summary>
public class RecurringDocumentTests : FinanceIntegrationTestBase
{
    private static DateTime Today() => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

    private async Task<Guid> CustomerAsync(string name = "Subscription Co")
    {
        var r = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = name, Currency = "USD" }));
        r.Succeeded.ShouldBeTrue(r.Message);
        return r.Data!.Id;
    }

    private async Task<CreateRecurringDocumentDto> MonthlyInvoiceAsync(
        DateTime startDate, decimal amount = 500m, int? anchorDay = null)
    {
        var customer = await CustomerAsync();
        var revenue = await AccountIdByCodeAsync("4100");
        return new CreateRecurringDocumentDto
        {
            Name = "Monthly retainer",
            Kind = RecurringDocKind.Invoice,
            PartyId = customer,
            Currency = "USD",
            Frequency = RecurrenceFrequency.Monthly,
            Interval = 1,
            AnchorDay = anchorDay,
            StartDate = startDate,
            DueDays = 30,
            Lines = [new CreateRecurringLineDto { AccountId = revenue, Quantity = 1, UnitPrice = amount }],
        };
    }

    private Task<Result<RecurringDocumentDto>> CreateAsync(CreateRecurringDocumentDto input)
        => InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.CreateAsync(input));

    private Task<Result<RecurringSweepResultDto>> SweepAsync(DateTime asOf)
        => InScopeAsync<IRecurringGeneratorService, Result<RecurringSweepResultDto>>(s => s.RunDueAsync(asOf));

    // ── 排期 ────────────────────────────────────────────────

    /// <summary>
    /// ★锚点 31 号落在短月时收到月末，而不是溢出到下月 1 号。
    /// </summary>
    /// <remarks>
    /// "每月最后一天开票"是真实存在的约定，多走一天等于把整个账期悄悄错开；
    /// 而下一期仍从锚点推，不会因为二月被夹到 28 就一路 28 下去。
    /// </remarks>
    [Fact]
    public void Schedule_MonthEndAnchor_ClampsInsteadOfOverflowing()
    {
        var schedule = new CalendarRecurrenceSchedule();
        var jan31 = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);

        var feb = schedule.Next(jan31, RecurrenceFrequency.Monthly, 1, anchorDay: 31);
        feb.ShouldBe(new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc));

        // 关键：二月之后回到 31 号，而不是继续 28 号。
        var mar = schedule.Next(feb, RecurrenceFrequency.Monthly, 1, anchorDay: 31);
        mar.ShouldBe(new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Schedule_IsStrictlyIncreasing_ForEveryFrequency()
    {
        var schedule = new CalendarRecurrenceSchedule();
        var start = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        foreach (var frequency in Enum.GetValues<RecurrenceFrequency>())
        {
            var next = schedule.Next(start, frequency, 1, anchorDay: null);
            next.ShouldBeGreaterThan(start, $"{frequency} must move forward");
        }
    }

    [Fact]
    public async Task Preview_ShowsTheNextDates_WithoutWritingAnything()
    {
        await SeedCoaAsync();
        var input = await MonthlyInvoiceAsync(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));

        var preview = await InScopeAsync<IRecurringDocumentService, Result<RecurrencePreviewDto>>(
            s => Task.FromResult(s.PreviewSchedule(input, 3)));

        preview.Succeeded.ShouldBeTrue(preview.Message);
        preview.Data!.Dates.Count.ShouldBe(3);
        preview.Data.Dates[0].ShouldBe(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));
        preview.Data.Dates[1].ShouldBe(new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc));

        // 零副作用。
        var page = await InScopeAsync<IRecurringDocumentService, Result<IPagedList<RecurringDocumentDto>>>(
            s => s.GetPagedAsync(new RecurringDocumentQueryDto()));
        page.Data!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Preview_RespectsTheEndDate()
    {
        await SeedCoaAsync();
        var input = await MonthlyInvoiceAsync(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));
        input.EndDate = new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc);

        var preview = await InScopeAsync<IRecurringDocumentService, Result<RecurrencePreviewDto>>(
            s => Task.FromResult(s.PreviewSchedule(input, 12)));

        // 1/15、2/15、3/15 —— 4/15 越过结束日。
        preview.Data!.Dates.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Create_InvertedEndDate_Rejected400()
    {
        await SeedCoaAsync();
        var input = await MonthlyInvoiceAsync(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        input.EndDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = await CreateAsync(input);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    // ── 生成 ────────────────────────────────────────────────

    /// <summary>
    /// 生成的是**真发票**，不是别的什么东西。
    /// </summary>
    [Fact]
    public async Task Sweep_GeneratesARealInvoiceDraft()
    {
        await SeedCoaAsync();
        var today = Today();
        var created = await CreateAsync(await MonthlyInvoiceAsync(today, 750m));
        created.Succeeded.ShouldBeTrue(created.Message);

        var sweep = await SweepAsync(today);

        sweep.Succeeded.ShouldBeTrue(sweep.Message);
        sweep.Data!.Generated.ShouldBe(1);
        var run = sweep.Data.Runs.Single();
        run.DocType.ShouldBe(FinanceSourceTypes.Invoice);
        run.Posted.ShouldBeFalse("the default is a draft, not a posted document");

        var invoice = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.GetAsync(run.DocId!.Value));
        invoice.Succeeded.ShouldBeTrue(invoice.Message);
        invoice.Data!.Total.ShouldBe(750m);
        invoice.Data.Status.ShouldBe(FinanceDocumentStatus.Draft);
        invoice.Data.DueDate.ShouldBe(today.AddDays(30));
    }

    /// <summary>
    /// ★出厂默认生成**草稿**：让日历直接往总账里写东西，是最容易到月底才被发现的错。
    /// </summary>
    [Fact]
    public async Task Sweep_DefaultsToDrafts_LeavingTheLedgerUntouched()
    {
        await SeedCoaAsync();
        var today = Today();
        await CreateAsync(await MonthlyInvoiceAsync(today, 400m));

        await SweepAsync(today);

        var tb = await InScopeAsync<IFinancialReportService, Result<TrialBalanceReportDto>>(
            s => s.GetTrialBalanceAsync(today.AddYears(-1), today));
        tb.Data!.Rows.Sum(r => r.PeriodDebit).ShouldBe(0m, "nothing was posted");
    }

    [Fact]
    public async Task Sweep_AutoPostOnTemplate_PostsAndNumbersTheDocument()
    {
        await SeedCoaAsync();
        var today = Today();
        var input = await MonthlyInvoiceAsync(today, 900m);
        input.AutoPost = true;
        await CreateAsync(input);

        var sweep = await SweepAsync(today);

        var run = sweep.Data!.Runs.Single();
        run.Posted.ShouldBeTrue();
        run.DocNumber.ShouldNotBeNullOrWhiteSpace("a posted invoice carries its number");

        var invoice = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.GetAsync(run.DocId!.Value));
        invoice.Data!.Status.ShouldBe(FinanceDocumentStatus.Posted);
    }

    /// <summary>
    /// ★★幂等：同一期次跑两次只出一张单据。
    /// </summary>
    /// <remarks>
    /// 给客户重复开一张发票是要打电话道歉的事故，所以这条是本模块最重要的断言。
    /// </remarks>
    [Fact]
    public async Task Sweep_RunTwice_DoesNotBillTwice()
    {
        await SeedCoaAsync();
        var today = Today();
        var created = await CreateAsync(await MonthlyInvoiceAsync(today, 300m));

        var first = await SweepAsync(today);
        first.Data!.Generated.ShouldBe(1);

        // 第二次扫描：排期已推到下个月，什么都不该发生。
        var second = await SweepAsync(today);
        second.Data!.Generated.ShouldBe(0);

        var invoices = await InScopeAsync<IInvoiceService, Result<IPagedList<InvoiceDto>>>(
            s => s.GetPagedAsync(new InvoiceQueryDto { PageSize = 50 }));
        invoices.Data!.Items.Count.ShouldBe(1);

        var runs = await InScopeAsync<IRecurringDocumentService, Result<IPagedList<RecurringRunDto>>>(
            s => s.GetRunsAsync(new RecurringRunQueryDto { RecurringDocumentId = created.Data!.Id }));
        runs.Data!.Items.Count(r => r.Status == RecurringRunStatus.Generated).ShouldBe(1);
    }

    /// <summary>
    /// ★手工触发同样受幂等键约束：重复点击是安全的。
    /// </summary>
    [Fact]
    public async Task RunOne_BeforeItIsDue_Rejected409()
    {
        await SeedCoaAsync();
        var today = Today();
        var created = await CreateAsync(await MonthlyInvoiceAsync(today.AddDays(10)));

        var run = await InScopeAsync<IRecurringGeneratorService, Result<RecurringSweepResultDto>>(
            s => s.RunOneAsync(created.Data!.Id, today));

        run.Succeeded.ShouldBeFalse();
        run.Code.ShouldBe(409);
    }

    /// <summary>
    /// 排期推进：跑完这一期，下一次落在一个月后。
    /// </summary>
    [Fact]
    public async Task Sweep_AdvancesTheSchedule()
    {
        await SeedCoaAsync();
        var today = Today();
        var created = await CreateAsync(await MonthlyInvoiceAsync(today));

        await SweepAsync(today);

        var after = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(
            s => s.GetAsync(created.Data!.Id));
        after.Data!.NextRunDate.ShouldBeGreaterThan(today);
        after.Data.OccurrenceCount.ShouldBe(1);
        after.Data.LastRunDate.ShouldBe(today);
    }

    /// <summary>
    /// 到达次数上限自动结束：一条不会再产出任何东西的模板挂在"运行中"里，
    /// 只会让人每个月重新判断一次它是不是坏了。
    /// </summary>
    [Fact]
    public async Task Sweep_ReachingMaxOccurrences_EndsTheTemplate()
    {
        await SeedCoaAsync();
        var today = Today();
        var input = await MonthlyInvoiceAsync(today);
        input.MaxOccurrences = 1;
        var created = await CreateAsync(input);

        await SweepAsync(today);

        var after = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(
            s => s.GetAsync(created.Data!.Id));
        after.Data!.Status.ShouldBe(RecurringStatus.Ended);
    }

    // ── 补齐策略 ────────────────────────────────────────────

    /// <summary>
    /// 默认 GenerateAll：作业停了三个月，三期都补出来。
    /// </summary>
    [Fact]
    public async Task CatchUp_GenerateAll_BacksFillEveryMissedPeriod()
    {
        await SeedCoaAsync();
        var today = Today();
        var start = today.AddMonths(-3);
        await CreateAsync(await MonthlyInvoiceAsync(start, 100m));

        var sweep = await SweepAsync(today);

        // 起始日 + 三个月内的每一期。
        sweep.Data!.Generated.ShouldBeGreaterThanOrEqualTo(3);
        sweep.Data.Skipped.ShouldBe(0);
    }

    [Fact]
    public async Task Sweep_PausedTemplate_GeneratesNothing()
    {
        await SeedCoaAsync();
        var today = Today();
        var created = await CreateAsync(await MonthlyInvoiceAsync(today));
        await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.PauseAsync(created.Data!.Id));

        var sweep = await SweepAsync(today);

        sweep.Data!.TemplatesDue.ShouldBe(0);
        sweep.Data.Generated.ShouldBe(0);
    }

    /// <summary>
    /// ★恢复不补暂停期间的期次：那些是被人为决定不要的。
    /// </summary>
    [Fact]
    public async Task Resume_DoesNotBackfillThePausedPeriods()
    {
        await SeedCoaAsync();
        var today = Today();
        // 起始日错开今天：恢复当天恰好落在期次上是另一件事（那一期是"现在"，不是补齐）。
        var created = await CreateAsync(await MonthlyInvoiceAsync(today.AddMonths(-4).AddDays(-3)));
        await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.PauseAsync(created.Data!.Id));

        var resumed = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(
            s => s.ResumeAsync(created.Data!.Id));

        resumed.Data!.Status.ShouldBe(RecurringStatus.Active);
        resumed.Data.NextRunDate.ShouldBeGreaterThan(today);

        var sweep = await SweepAsync(today);
        sweep.Data!.Generated.ShouldBe(0, "the paused periods are gone for good");
    }

    // ── 模板管理 ────────────────────────────────────────────

    /// <summary>
    /// 已经生成过单据的模板不可删：那些单据的来历会因此无从查起。
    /// </summary>
    [Fact]
    public async Task Delete_AfterItHasGenerated_Rejected409()
    {
        await SeedCoaAsync();
        var today = Today();
        var created = await CreateAsync(await MonthlyInvoiceAsync(today));
        await SweepAsync(today);

        var deleted = await InScopeAsync<IRecurringDocumentService, Result>(s => s.DeleteAsync(created.Data!.Id));

        deleted.Succeeded.ShouldBeFalse();
        deleted.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Delete_BeforeAnythingRan_Succeeds()
    {
        await SeedCoaAsync();
        var created = await CreateAsync(await MonthlyInvoiceAsync(Today().AddMonths(1)));

        var deleted = await InScopeAsync<IRecurringDocumentService, Result>(s => s.DeleteAsync(created.Data!.Id));

        deleted.Succeeded.ShouldBeTrue(deleted.Message);
    }

    [Fact]
    public async Task Create_UnknownCustomer_Returns404()
    {
        await SeedCoaAsync();
        var input = await MonthlyInvoiceAsync(Today());
        input.PartyId = Guid.NewGuid();

        var result = await CreateAsync(input);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task Create_NoLines_Rejected400()
    {
        await SeedCoaAsync();
        var input = await MonthlyInvoiceAsync(Today());
        input.Lines = [];

        var result = await CreateAsync(input);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>
    /// 改价不改排期：涨个价不该让下一期悄悄挪到别的日子。
    /// </summary>
    [Fact]
    public async Task Update_PriceOnly_LeavesTheScheduleAlone()
    {
        await SeedCoaAsync();
        var today = Today();
        var created = await CreateAsync(await MonthlyInvoiceAsync(today.AddDays(5), 200m));
        var before = created.Data!.NextRunDate;
        var revenue = await AccountIdByCodeAsync("4100");

        var updated = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(
            s => s.UpdateAsync(created.Data.Id, new UpdateRecurringDocumentDto
            {
                Name = created.Data.Name,
                PartyId = created.Data.PartyId,
                Currency = created.Data.Currency,
                Frequency = created.Data.Frequency,
                Interval = created.Data.Interval,
                AnchorDay = created.Data.AnchorDay,
                StartDate = created.Data.StartDate,
                DueDays = created.Data.DueDays,
                ConcurrencyStamp = created.Data.ConcurrencyStamp,
                Lines = [new CreateRecurringLineDto { AccountId = revenue, Quantity = 1, UnitPrice = 260m }],
            }));

        updated.Succeeded.ShouldBeTrue(updated.Message);
        updated.Data!.NextRunDate.ShouldBe(before);
        updated.Data.EstimatedTotal.ShouldBe(260m);
    }
}
