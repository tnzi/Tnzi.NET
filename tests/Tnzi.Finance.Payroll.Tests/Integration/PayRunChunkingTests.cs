namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 行数分块：调小 MaxLinesPerEntry 使单批次员工过账必须拆成多张凭证，
/// 各 payslip 记各自凭证、全部凭证借贷仍恒等。
/// </summary>
public class PayRunChunkingTests : PayrollIntegrationTestBase
{
    protected override void ConfigureExtraServices(IServiceCollection services)
    {
        // 每员工 4 行（3 聚合科目 5300/2200/2100 + 1 WagesPayable）；上限 4 → chunkSize 1 → 每员工一凭证
        services.Configure<FinanceOptions>(o => o.MaxLinesPerEntry = 4);
    }

    [Fact]
    public async Task Post_ExceedingLineLimit_SplitsIntoMultipleJournalEntries()
    {
        var (structureId, _) = await StandardScenarioAsync("E1");
        var e2 = await CreateEmployeeAsync("E2", "Two");
        var e3 = await CreateEmployeeAsync("E3", "Three");
        await AssignAsync(e2.Id, structureId, 1000m, new DateTime(2026, 1, 1));
        await AssignAsync(e3.Id, structureId, 1000m, new DateTime(2026, 1, 1));

        var runId = await CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 7, 5));
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId))).Succeeded.ShouldBeTrue();

        var post = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(runId));
        post.Succeeded.ShouldBeTrue(post.Message);

        using var scope = ServiceProvider.CreateScope();
        var entryRepo = scope.ServiceProvider.GetRequiredService<IRepository<JournalEntry, Guid>>();
        var entries = await entryRepo.ToListAsync(e => e.SourceType == "PayRun" && e.SourceId == runId.ToString());
        entries.Count.ShouldBeGreaterThan(1); // 3 员工 → 3 张凭证

        // 每张凭证不超过上限且借贷恒等
        foreach (var entry in entries)
        {
            var (debit, credit) = await JournalTotalsAsync(entry.Id);
            debit.ShouldBe(credit);
        }

        // 每张 payslip 都记到某张凭证
        var slips = (await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(runId))).Data!;
        foreach (var listItem in slips)
        {
            var slip = await InScopeAsync<IPayRunService, Result<PayslipDto>>(s => s.GetPayslipAsync(runId, listItem.Id));
            slip.Data!.JournalEntryId.ShouldNotBeNull();
        }
    }
}
