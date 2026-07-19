namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 圈选规则（结构过滤/离职排除/无分配排除）与状态机门（草稿不可过账、未过账不可付款、已过账不可删）。
/// </summary>
public class PayRunEligibilityAndGuardTests : PayrollIntegrationTestBase
{
    private static readonly DateTime PeriodStart = new(2026, 6, 1);
    private static readonly DateTime PeriodEnd = new(2026, 6, 30);
    private static readonly DateTime PayDate = new(2026, 7, 5);

    [Fact]
    public async Task Calculate_StructureFilter_SelectsOnlyMatchingEmployees()
    {
        var (standardStructureId, _) = await StandardScenarioAsync("A");
        // 第二个员工用另一结构（独立组件，避免与 BASIC 撞码）
        var wage = await ComponentWithAccountsAsync("WAGE", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var other = await CreateStructureAsync("Other", new SalaryStructureLineInputDto { ComponentId = wage, Sequence = 1 });
        other.Succeeded.ShouldBeTrue(other.Message);
        var b = await CreateEmployeeAsync("B", "Bee");
        await AssignAsync(b.Id, other.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate, structureId: standardStructureId);
        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId));
        calc.Succeeded.ShouldBeTrue(calc.Message);
        calc.Data!.EmployeeCount.ShouldBe(1); // 仅 A（Standard 结构），B 被结构过滤排除
    }

    [Fact]
    public async Task Calculate_TerminatedBeforePeriodStart_Excluded()
    {
        var (structureId, employeeId) = await StandardScenarioAsync();
        // 员工在期初前离职
        await InScopeAsync<IEmployeeService, Result<EmployeeDto>>(s => s.UpdateAsync(employeeId, new UpdateEmployeeDto
        {
            Code = "EMP1",
            Name = "One",
            TerminationDate = new DateTime(2026, 5, 15),
            IsActive = true
        }));
        _ = structureId;

        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId));
        calc.Succeeded.ShouldBeFalse(); // 唯一员工被离职日期排除 → 无人可算
        calc.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Post_DraftRun_Rejected()
    {
        await StandardScenarioAsync();
        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        // 未计算直接过账
        var post = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(runId));
        post.Succeeded.ShouldBeFalse();
        post.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Pay_BeforePost_Rejected()
    {
        await StandardScenarioAsync();
        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId))).Succeeded.ShouldBeTrue();

        var bankId = await AccountIdByCodeAsync("1120");
        var pay = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PayAsync(runId, new PayRunPaymentDto
        {
            PaymentAccountId = bankId,
            PaymentDate = PayDate
        }));
        pay.Succeeded.ShouldBeFalse(); // 未过账不可付款
        pay.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Delete_PostedRun_Rejected()
    {
        await StandardScenarioAsync();
        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId))).Succeeded.ShouldBeTrue();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(runId))).Succeeded.ShouldBeTrue();

        var del = await InScopeAsync<IPayRunService, Result>(s => s.DeleteAsync(runId));
        del.Succeeded.ShouldBeFalse(); // 已过账不可删，只能作废
        del.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Delete_DraftRun_Succeeds()
    {
        await StandardScenarioAsync();
        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId))).Succeeded.ShouldBeTrue();
        // 计算后回到可删？ 不——只有 Draft 可删。这里验证 Draft 删除路径：新建一个未计算的 run
        var draftId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        var del = await InScopeAsync<IPayRunService, Result>(s => s.DeleteAsync(draftId));
        del.Succeeded.ShouldBeTrue(del.Message);
    }
}
