namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 组件编码一旦出现在工资单上就不可再改。
/// </summary>
/// <remarks>
/// 被保护的缺陷：YTD 累计按 <c>PayslipLine.ComponentCode</c> 的字符串快照聚合，<c>Ytd('CODE')</c> 也按字符串查，
/// 查不到就取 0（这个员工今年还没有过这一项，是正常的）。于是年中把 CPP_EMP 改名成 CPP_EMPLOYEE 之后，
/// <c>Ytd('CPP_EMPLOYEE')</c> 只含改名后的批次，改名前已扣的供款全部消失于基数 —— 法定上限永不封顶，
/// 没有任何报错。判据与「被结构行引用禁删」相同：有历史就拒绝，另建一个组件。
/// </remarks>
public class SalaryComponentRenameTests : PayrollIntegrationTestBase
{
    private static readonly DateTime PeriodStart = new(2026, 6, 1);
    private static readonly DateTime PeriodEnd = new(2026, 6, 30);
    private static readonly DateTime PayDate = new(2026, 7, 5);

    private Task<Result<SalaryComponentDto>> RenameAsync(Guid id, string newCode)
        => InScopeAsync<ISalaryComponentService, Result<SalaryComponentDto>>(s => s.UpdateAsync(id, new UpdateSalaryComponentDto
        {
            Code = newCode,
            Name = "Renamed",
            Type = SalaryComponentType.Earning,
            Formula = "BASE",
            IsActive = true
        }));

    [Fact]
    public async Task Update_RenameCode_AllowedBeforeAnyPayslip()
    {
        var created = await CreateComponentAsync("BASIC", formula: "BASE");

        var renamed = await RenameAsync(created.Id, "BASIC_PAY");

        renamed.Succeeded.ShouldBeTrue(renamed.Message);
        renamed.Data!.Code.ShouldBe("BASIC_PAY");
    }

    [Fact]
    public async Task Update_RenameCode_IsRejectedOnceComponentHasPayslipLines()
    {
        var (structureId, _) = await StandardScenarioAsync();
        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate, structureId);
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId))).Succeeded.ShouldBeTrue();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(runId))).Succeeded.ShouldBeTrue();

        var basic = await InScopeAsync<ISalaryComponentService, SalaryComponent?>(s => s.FindByCodeAsync("BASIC"));
        basic.ShouldNotBeNull();

        var renamed = await RenameAsync(basic.Id, "BASIC_PAY");

        renamed.Succeeded.ShouldBeFalse("renaming would silently drop every prior run from Ytd('BASIC_PAY')");
        renamed.Code.ShouldBe(409);
        (await ReloadAsync<SalaryComponent>(basic.Id))!.Code.ShouldBe("BASIC");
    }

    /// <summary>
    /// 同一编码原样回传（普通编辑）仍然放行：拒绝的是改名，不是改动。
    /// </summary>
    [Fact]
    public async Task Update_SameCode_StillAllowedWithHistory()
    {
        var (structureId, _) = await StandardScenarioAsync();
        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate, structureId);
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId))).Succeeded.ShouldBeTrue();

        var basic = await InScopeAsync<ISalaryComponentService, SalaryComponent?>(s => s.FindByCodeAsync("BASIC"));
        var edited = await RenameAsync(basic!.Id, "basic");

        edited.Succeeded.ShouldBeTrue(edited.Message);
        edited.Data!.Name.ShouldBe("Renamed");
    }

    /// <summary>
    /// 端到端：改名被拒之后，下一期的 Ytd() 仍然含改名前的累计。
    /// </summary>
    [Fact]
    public async Task Ytd_AfterRejectedRename_StillIncludesPriorRuns()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var prior = await ComponentWithAccountsAsync("PRIOR", SalaryComponentType.Earning, "Ytd('BASIC')", expenseAccountCode: "5300");
        var structure = await CreateStructureAsync("Ytd",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = prior, Sequence = 2 });
        structure.Succeeded.ShouldBeTrue(structure.Message);
        var emp = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(emp.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        var may = await CreateRunAsync(new DateTime(2026, 5, 1), new DateTime(2026, 5, 31), new DateTime(2026, 5, 31));
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(may))).Succeeded.ShouldBeTrue();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(may))).Succeeded.ShouldBeTrue();

        (await RenameAsync(basic, "BASIC_PAY")).Succeeded.ShouldBeFalse();

        var june = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(june));
        calc.Succeeded.ShouldBeTrue(calc.Message);
        // BASIC 1000 + PRIOR = Ytd('BASIC') = 1000（五月）→ 2000
        calc.Data!.GrossTotal.ShouldBe(2000m);
    }
}
