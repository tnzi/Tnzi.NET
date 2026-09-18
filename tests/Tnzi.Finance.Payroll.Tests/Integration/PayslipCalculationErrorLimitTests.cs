namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// <c>Payslip.CalculationError</c> 必须装得进它那 1000 字符的列。
/// </summary>
/// <remarks>
/// SQLite 不检查列宽，所以这里断言的是**长度**：在 SQL Server / PostgreSQL 上超宽的赋值会让
/// <c>CalculateAsync</c> 的 <c>InsertManyAsync</c> 抛 <c>DbUpdateException</c>，整批回滚 500，
/// 批次停在 Draft 且没有任何解释 —— 那条本该把问题摆到操作员面前的消息，正是存不进去的东西。
/// </remarks>
public class PayslipCalculationErrorLimitTests : PayrollIntegrationTestBase
{
    private static readonly DateTime PeriodStart = new(2026, 6, 1);
    private static readonly DateTime PeriodEnd = new(2026, 6, 30);
    private static readonly DateTime PayDate = new(2026, 7, 5);

    protected override void ConfigureExtraServices(IServiceCollection services)
    {
        services.AddScoped<IPayslipCalculationHook, LongWindedHook>();
    }

    private async Task<PayslipDto> SinglePayslipAsync(Guid runId)
    {
        var list = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(runId));
        list.Succeeded.ShouldBeTrue(list.Message);
        var slip = await InScopeAsync<IPayRunService, Result<PayslipDto>>(s => s.GetPayslipAsync(runId, list.Data!.Single().Id));
        slip.Succeeded.ShouldBeTrue(slip.Message);
        return slip.Data!;
    }

    [Fact]
    public async Task Calculate_HookReturnsAHugeMessage_PersistsAClampedError()
    {
        LongWindedHook.Message = new string('h', 5000);
        try
        {
            await StandardScenarioAsync();
            var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);

            var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId));
            calc.Succeeded.ShouldBeTrue(calc.Message);
            calc.Data!.ErrorCount.ShouldBe(1);

            var slip = await SinglePayslipAsync(runId);
            slip.CalculationError.ShouldNotBeNull();
            slip.CalculationError.Length.ShouldBeLessThanOrEqualTo(PayslipFieldLimits.CalculationErrorMaxLength);
        }
        finally
        {
            LongWindedHook.Message = null;
        }
    }

    /// <summary>
    /// 孤儿输入很多时，消息列出前几笔并汇总其余，而不是逐笔罗列到超宽。
    /// </summary>
    [Fact]
    public async Task Calculate_ManyOrphanedInputs_SummarisesInsteadOfListingEveryOne()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");

        const int orphanCount = 12;
        var inputComponents = new List<Guid>();
        var lines = new List<SalaryStructureLineInputDto> { new() { ComponentId = basic, Sequence = 1 } };
        for (var i = 0; i < orphanCount; i++)
        {
            var id = await ComponentWithAccountsAsync($"ONE_TIME_ALLOWANCE_COMPONENT_{i:00}", SalaryComponentType.Earning, "Input()",
                expenseAccountCode: "5300", condition: "Input() > 0");
            inputComponents.Add(id);
            lines.Add(new SalaryStructureLineInputDto { ComponentId = id, Sequence = i + 2 });
        }

        var withInputs = await CreateStructureAsync("With inputs", lines.ToArray());
        withInputs.Succeeded.ShouldBeTrue(withInputs.Message);
        var withoutInputs = await CreateStructureAsync("Without inputs", new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 });
        withoutInputs.Succeeded.ShouldBeTrue(withoutInputs.Message);

        var employee = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(employee.Id, withInputs.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        foreach (var componentId in inputComponents)
        {
            var set = await InScopeAsync<IPayRunService, Result<PayRunInputDto>>(s => s.SetInputAsync(runId, new SetPayRunInputDto
            {
                EmployeeId = employee.Id,
                ComponentId = componentId,
                Amount = 123.45m,
                Note = "a fairly long note so that each entry carries some weight in the message"
            }));
            set.Succeeded.ShouldBeTrue(set.Message);
        }

        // 录完之后把员工换到一个不含任何输入组件的结构：全部输入变成孤儿
        await AssignAsync(employee.Id, withoutInputs.Data!.Id, 1000m, new DateTime(2026, 5, 1));

        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId));
        calc.Succeeded.ShouldBeTrue(calc.Message);
        calc.Data!.ErrorCount.ShouldBe(1);

        var slip = await SinglePayslipAsync(runId);
        slip.CalculationError.ShouldNotBeNull();
        slip.CalculationError.Length.ShouldBeLessThanOrEqualTo(PayslipFieldLimits.CalculationErrorMaxLength);
        slip.CalculationError.ShouldContain("ONE_TIME_ALLOWANCE_COMPONENT_00");
        slip.CalculationError.ShouldContain($"and {orphanCount - PayslipFieldLimits.OrphanedInputsListed} more");
        slip.CalculationError.ShouldNotContain("ONE_TIME_ALLOWANCE_COMPONENT_11", customMessage: "the tail of the list is summarised, not spelled out");
    }

    private sealed class LongWindedHook : IPayslipCalculationHook
    {
        public static string? Message { get; set; }

        public Task<Result> AfterCalculateAsync(PayslipCalculationContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(Message == null ? Result.Success() : Result.Failure(Message, 400));
    }
}
