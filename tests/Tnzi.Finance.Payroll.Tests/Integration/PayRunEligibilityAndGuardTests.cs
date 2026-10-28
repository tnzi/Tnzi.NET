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

    /// <summary>
    /// ★入职日与离职日对称：期末之后才入职的不进这一期；期间内入职的照常入选。
    /// </summary>
    /// <remarks>
    /// 管理端新建分配时生效日默认为当天，提前录入的新人因此带着一条「已生效」的分配
    /// 进入入职之前的那一期，此前圈选只看分配与离职日，于是按整期发了一份工资。
    /// </remarks>
    [Fact]
    public async Task Calculate_HiredAfterThePeriodEnd_Excluded_HiredWithinThePeriod_Included()
    {
        var (structureId, _) = await StandardScenarioAsync("EMP1");
        var future = await CreateHiredEmployeeAsync("NEW", new DateTime(2026, 7, 1));
        await AssignAsync(future.Id, structureId, 1000m, new DateTime(2026, 6, 10));
        var midPeriod = await CreateHiredEmployeeAsync("MID", new DateTime(2026, 6, 15));
        await AssignAsync(midPeriod.Id, structureId, 1000m, new DateTime(2026, 6, 10));

        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId));

        calc.Succeeded.ShouldBeTrue(calc.Message);
        calc.Data!.EmployeeCount.ShouldBe(2, "EMP1 and MID are on the payroll in June; NEW starts in July");
        (await CountAsync<Payslip>(p => p.PayRunId == runId && p.EmployeeId == future.Id)).ShouldBe(0);
        (await CountAsync<Payslip>(p => p.PayRunId == runId && p.EmployeeId == midPeriod.Id)).ShouldBe(1);
    }

    /// <summary>录入端与计算端同一判据：给一个还没入职的人录一笔钱，当场 400 并说清原因。</summary>
    [Fact]
    public async Task SetInput_ForAnEmployeeNotYetHired_Rejected400()
    {
        var (structureId, _) = await StandardScenarioAsync("EMP1");
        var future = await CreateHiredEmployeeAsync("NEW", new DateTime(2026, 7, 1));
        await AssignAsync(future.Id, structureId, 1000m, new DateTime(2026, 6, 10));
        var bonus = await ComponentWithAccountsAsync("BONUS", SalaryComponentType.Earning, "INPUT()", expenseAccountCode: "5300");
        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);

        var set = await InScopeAsync<IPayRunService, Result<PayRunInputDto>>(s => s.SetInputAsync(runId, new SetPayRunInputDto
        {
            EmployeeId = future.Id,
            ComponentId = bonus,
            Amount = 500m
        }));

        set.Succeeded.ShouldBeFalse();
        set.Code.ShouldBe(400);
        set.Message.ShouldNotBeNull().ShouldContain("hire date");
    }

    /// <summary>
    /// ★不选结构过滤的月薪批次不得把周薪结构上的员工圈进来。
    /// </summary>
    /// <remarks>
    /// 批次只有一个 PERIODS_PER_YEAR（取自批次频率）：周薪员工被按 12 期/年算一整月并过账，
    /// 而他们的周薪批次还会再发一遍，没有任何报错。
    /// </remarks>
    [Fact]
    public async Task Calculate_MonthlyRunWithoutAStructureFilter_LeavesWeeklyStructureEmployeesOut()
    {
        await StandardScenarioAsync("MONTHLY");
        var weekly = await CreateWeeklyStructureAsync();
        var weeklyEmployee = await CreateEmployeeAsync("WEEKLY", "Weekly Paid");
        await AssignAsync(weeklyEmployee.Id, weekly, 800m, new DateTime(2026, 1, 1));

        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId));

        calc.Succeeded.ShouldBeTrue(calc.Message);
        calc.Data!.EmployeeCount.ShouldBe(1);
        (await CountAsync<Payslip>(p => p.PayRunId == runId && p.EmployeeId == weeklyEmployee.Id)).ShouldBe(0,
            "a weekly employee computed with PERIODS_PER_YEAR = 12 gets a month of pay on top of their weekly runs");
    }

    [Fact]
    public async Task SetInput_ForAWeeklyEmployeeOnAMonthlyRun_Rejected400()
    {
        await StandardScenarioAsync("MONTHLY");
        var weekly = await CreateWeeklyStructureAsync();
        var weeklyEmployee = await CreateEmployeeAsync("WEEKLY", "Weekly Paid");
        await AssignAsync(weeklyEmployee.Id, weekly, 800m, new DateTime(2026, 1, 1));
        var bonus = await ComponentWithAccountsAsync("BONUS", SalaryComponentType.Earning, "INPUT()", expenseAccountCode: "5300");
        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);

        var set = await InScopeAsync<IPayRunService, Result<PayRunInputDto>>(s => s.SetInputAsync(runId, new SetPayRunInputDto
        {
            EmployeeId = weeklyEmployee.Id,
            ComponentId = bonus,
            Amount = 500m
        }));

        set.Succeeded.ShouldBeFalse();
        set.Code.ShouldBe(400);
        set.Message.ShouldNotBeNull().ShouldContain("different frequency");
    }

    /// <summary>频率与自己的结构过滤不一致的批次一个人也圈不到：建单时就说清楚，不等到计算时才报「无人合格」。</summary>
    [Fact]
    public async Task CreateRun_WithAStructureFilterPaidAtAnotherFrequency_Rejected400()
    {
        await StandardScenarioAsync();
        var weekly = await CreateWeeklyStructureAsync();

        var result = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CreateAsync(new CreatePayRunDto
        {
            PeriodStart = PeriodStart,
            PeriodEnd = PeriodEnd,
            PayDate = PayDate,
            Frequency = PayFrequency.Monthly,
            StructureId = weekly
        }));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    private async Task<EmployeeDto> CreateHiredEmployeeAsync(string code, DateTime hireDate)
    {
        var result = await InScopeAsync<IEmployeeService, Result<EmployeeDto>>(s => s.CreateAsync(new CreateEmployeeDto
        {
            Code = code,
            Name = code,
            HireDate = hireDate
        }));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!;
    }

    private async Task<Guid> CreateWeeklyStructureAsync()
    {
        var wage = await ComponentWithAccountsAsync("WAGE", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var result = await InScopeAsync<ISalaryStructureService, Result<SalaryStructureDto>>(s => s.CreateAsync(new CreateSalaryStructureDto
        {
            Name = "Weekly",
            Frequency = PayFrequency.Weekly,
            Lines = [new SalaryStructureLineInputDto { ComponentId = wage, Sequence = 1 }]
        }));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Id;
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
        var draftId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        var del = await InScopeAsync<IPayRunService, Result>(s => s.DeleteAsync(draftId));
        del.Succeeded.ShouldBeTrue(del.Message);
    }

    /// <summary>
    /// 已计算但未过账的批次同样可删：它没有任何凭证，删掉是安全的；而不让删，
    /// 摄取错了的 OpeningBalance / 带错的 External 批次就没有任何退出路径。
    /// </summary>
    [Fact]
    public async Task Delete_CalculatedRun_Succeeds_AndTakesItsPayslipsWithIt()
    {
        await StandardScenarioAsync();
        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId))).Succeeded.ShouldBeTrue();
        (await CountAsync<Payslip>(p => p.PayRunId == runId)).ShouldBe(1);

        var del = await InScopeAsync<IPayRunService, Result>(s => s.DeleteAsync(runId));
        del.Succeeded.ShouldBeTrue(del.Message);

        (await ReloadAsync<PayRun>(runId)).ShouldBeNull();
        (await CountAsync<Payslip>(p => p.PayRunId == runId)).ShouldBe(0);
    }
}
