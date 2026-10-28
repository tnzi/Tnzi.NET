namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 单张工资单的出勤天数修正有上下界：不能为负，也不能超过期间的日历天数。
/// </summary>
/// <remarks>
/// 按出勤折算的公式（<c>BASE * WORKED_DAYS / PERIOD_DAYS</c>）对上界没有任何保护：30 天的月份把 30 录成 300，
/// 就是十倍工资，不报错，确认后照常过账。
/// </remarks>
public class PayrollInputBoundsTests : PayrollIntegrationTestBase
{
    private static readonly DateTime PeriodStart = new(2026, 6, 1);
    private static readonly DateTime PeriodEnd = new(2026, 6, 30);
    private static readonly DateTime PayDate = new(2026, 7, 5);

    private async Task<(Guid RunId, Guid PayslipId)> ProratedPayslipAsync()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning,
            "round(BASE * WORKED_DAYS / PERIOD_DAYS, 2)", expenseAccountCode: "5300");
        var structure = await CreateStructureAsync("Prorated", new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 });
        structure.Succeeded.ShouldBeTrue(structure.Message);
        var employee = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(employee.Id, structure.Data!.Id, 3000m, new DateTime(2026, 1, 1));

        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId));
        calc.Succeeded.ShouldBeTrue(calc.Message);
        var slips = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(runId));
        return (runId, slips.Data!.Single().Id);
    }

    private Task<Result<PayslipDto>> UpdateWorkedDaysAsync(Guid runId, Guid payslipId, decimal workedDays)
        => InScopeAsync<IPayRunService, Result<PayslipDto>>(
            s => s.UpdatePayslipInputsAsync(runId, payslipId, new UpdatePayslipInputsDto { WorkedDays = workedDays }));

    [Fact]
    public async Task WorkedDays_BeyondThePeriod_Rejected400_AndThePayslipIsUnchanged()
    {
        var (runId, payslipId) = await ProratedPayslipAsync();

        var result = await UpdateWorkedDaysAsync(runId, payslipId, 300m);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("30");
        var payslip = (await ReloadAsync<Payslip>(payslipId))!;
        payslip.WorkedDays.ShouldBe(30m);
        payslip.GrossPay.ShouldBe(3000m);
    }

    [Theory]
    [InlineData(30, 3000)]
    [InlineData(0, 0)]
    public async Task WorkedDays_WithinThePeriod_IsAccepted(int workedDays, int expectedGross)
    {
        var (runId, payslipId) = await ProratedPayslipAsync();

        var result = await UpdateWorkedDaysAsync(runId, payslipId, workedDays);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.GrossPay.ShouldBe(expectedGross);
    }
}
