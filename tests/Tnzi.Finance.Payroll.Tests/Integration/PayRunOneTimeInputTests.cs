namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 一次性输入（<see cref="PayRunInput"/> + 公式函数 <c>Input()</c>）：
/// 本批次 × 本员工 × 本组件的临时金额 —— 奖金、补发、罚扣、预支归还、更正。
///
/// ★三条不可让步的性质，每条对应一种"数字会错"的失效：
/// ① 进入**按序求值**（奖金在 GROSS 里，社保与个税据此算），不是算完之后追加的调整行；
/// ② 整批重算不丢（挂在批次上而不是会被重建的 payslip 上）；
/// ③ 录入了却没人读它，在录入那一刻就 400 —— 一笔沉默失效的奖金比一个报错危险得多。
/// </summary>
public class PayRunOneTimeInputTests : PayrollIntegrationTestBase
{
    /// <summary>
    /// 让重算在测试里确定性地失败的旋钮（<see cref="PayrollOptions.MaxEmployeesPerRun"/>）。
    /// 现实中重算失败是并发 409，单线程测试里造不出来；这个上限走的是同一条返回路径。
    /// </summary>
    private int _maxEmployeesPerRun = 1000;

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        // IOptionsSnapshot 按 scope 重建，所以委托在每次 InScopeAsync 时重读当前值。
        services.Configure<PayrollOptions>(o => o.MaxEmployeesPerRun = _maxEmployeesPerRun);
    }

    /// <summary>
    /// BASIC(Earning=BASE) + BONUS(Earning=Input(), 仅在录入时出行) + TAX(Deduction=GROSS×10%)。
    /// base=1000 时无奖金：gross 1000 / tax 100 / net 900。
    /// </summary>
    private async Task<(Guid StructureId, Guid EmployeeId, Guid BonusId, Guid TaxId)> BonusScenarioAsync()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var bonus = await ComponentWithAccountsAsync("BONUS", SalaryComponentType.Earning, "Input()",
            expenseAccountCode: "5300", condition: "Input() > 0");
        var tax = await ComponentWithAccountsAsync("TAX", SalaryComponentType.Deduction, "GROSS * 0.10", liabilityAccountCode: "2200");

        var structure = await CreateStructureAsync("Bonus",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = bonus, Sequence = 2 },
            new SalaryStructureLineInputDto { ComponentId = tax, Sequence = 3 });
        structure.Succeeded.ShouldBeTrue(structure.Message);

        var employee = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(employee.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));
        return (structure.Data!.Id, employee.Id, bonus, tax);
    }

    private Task<Guid> CreateJuneRunAsync()
        => CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 6, 30));

    private async Task<PayslipDto> SinglePayslipAsync(Guid runId)
    {
        var list = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(runId));
        list.Succeeded.ShouldBeTrue(list.Message);
        var slip = await InScopeAsync<IPayRunService, Result<PayslipDto>>(s => s.GetPayslipAsync(runId, list.Data!.Single().Id));
        slip.Succeeded.ShouldBeTrue(slip.Message);
        return slip.Data!;
    }

    private Task<Result<PayRunInputDto>> SetInputAsync(Guid runId, Guid employeeId, Guid componentId, decimal amount, string? note = null)
        => InScopeAsync<IPayRunService, Result<PayRunInputDto>>(s => s.SetInputAsync(runId, new SetPayRunInputDto
        {
            EmployeeId = employeeId,
            ComponentId = componentId,
            Amount = amount,
            Note = note
        }));

    // ---------- ① 进入按序求值 ----------

    [Fact]
    public async Task OneTimeInput_IsInsideGross_SoDeductionsAreComputedOffIt()
    {
        var (_, employeeId, bonusId, _) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run))).Succeeded.ShouldBeTrue();

        // 没录入之前：条件不成立，奖金行根本不出现。
        var before = await SinglePayslipAsync(run);
        before.Lines.ShouldNotContain(l => l.ComponentCode == "BONUS");
        before.GrossPay.ShouldBe(1000m);
        before.TotalDeductions.ShouldBe(100m);

        var set = await SetInputAsync(run, employeeId, bonusId, 5000m, "2026 H1 performance");
        set.Succeeded.ShouldBeTrue(set.Message);

        var after = await SinglePayslipAsync(run);
        var bonusLine = after.Lines.Single(l => l.ComponentCode == "BONUS");
        bonusLine.Amount.ShouldBe(5000m);
        // ★ 奖金进了 GROSS，所以税是 600 而不是 100 —— 这正是"追加一条调整行"做不到的：
        // 那样净额也对得上，却错过了应计、应保、应税三件事。
        after.GrossPay.ShouldBe(6000m);
        after.TotalDeductions.ShouldBe(600m);
        after.NetPay.ShouldBe(5400m);
    }

    [Fact]
    public async Task OneTimeInput_IsVisibleOnThePayslipLine_NotOnlyInTheTotal()
    {
        var (_, employeeId, bonusId, _) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run))).Succeeded.ShouldBeTrue();
        (await SetInputAsync(run, employeeId, bonusId, 5000m)).Succeeded.ShouldBeTrue();

        var slip = await SinglePayslipAsync(run);
        // 公式可以是 BASE + Input()，单看 Amount 分不出哪部分是一次性的。
        slip.Lines.Single(l => l.ComponentCode == "BONUS").InputAmount.ShouldBe(5000m);
        slip.Lines.Single(l => l.ComponentCode == "BASIC").InputAmount.ShouldBeNull();

        var inputs = await InScopeAsync<IPayRunService, Result<List<PayRunInputDto>>>(s => s.GetInputsAsync(run));
        var entry = inputs.Data!.Single();
        entry.ComponentCode.ShouldBe("BONUS");
        entry.EmployeeCode.ShouldBe("EMP1");
        entry.Amount.ShouldBe(5000m);
    }

    // ---------- ② 重算不丢 ----------

    [Fact]
    public async Task OneTimeInput_SurvivesAFullRecalculationOfTheRun()
    {
        var (_, employeeId, bonusId, _) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run))).Succeeded.ShouldBeTrue();
        (await SetInputAsync(run, employeeId, bonusId, 5000m)).Succeeded.ShouldBeTrue();

        // 整批重算会软删旧 payslip 重建 —— 挂在 payslip 上的输入会在这里消失。
        var recalc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run));
        recalc.Succeeded.ShouldBeTrue(recalc.Message);

        var slip = await SinglePayslipAsync(run);
        slip.Lines.Single(l => l.ComponentCode == "BONUS").Amount.ShouldBe(5000m);
        slip.GrossPay.ShouldBe(6000m);
    }

    [Fact]
    public async Task OneTimeInput_CanBeEnteredBeforeTheFirstCalculation()
    {
        var (_, employeeId, bonusId, _) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();

        // 真实发薪的顺序：先收齐本期的一次性事项，再跑批。
        (await SetInputAsync(run, employeeId, bonusId, 250m)).Succeeded.ShouldBeTrue();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run))).Succeeded.ShouldBeTrue();

        var slip = await SinglePayslipAsync(run);
        slip.Lines.Single(l => l.ComponentCode == "BONUS").Amount.ShouldBe(250m);
    }

    [Fact]
    public async Task OneTimeInput_DoesNotResetAWorkedDaysCorrection()
    {
        var (_, employeeId, bonusId, _) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run))).Succeeded.ShouldBeTrue();

        var slip = await SinglePayslipAsync(run);
        var updated = await InScopeAsync<IPayRunService, Result<PayslipDto>>(s => s.UpdatePayslipInputsAsync(run, slip.Id,
            new UpdatePayslipInputsDto { WorkedDays = 12m }));
        updated.Succeeded.ShouldBeTrue(updated.Message);

        (await SetInputAsync(run, employeeId, bonusId, 500m)).Succeeded.ShouldBeTrue();

        // 录一笔奖金不该把已经改过的出勤天数冲回周期天数（30）。
        (await SinglePayslipAsync(run)).WorkedDays.ShouldBe(12m);
    }

    [Fact]
    public async Task OneTimeInput_Delete_TakesTheLineBackOut()
    {
        var (_, employeeId, bonusId, _) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run))).Succeeded.ShouldBeTrue();
        var set = await SetInputAsync(run, employeeId, bonusId, 5000m);
        set.Succeeded.ShouldBeTrue(set.Message);

        var deleted = await InScopeAsync<IPayRunService, Result>(s => s.DeleteInputAsync(run, set.Data!.Id));
        deleted.Succeeded.ShouldBeTrue(deleted.Message);

        var slip = await SinglePayslipAsync(run);
        slip.Lines.ShouldNotContain(l => l.ComponentCode == "BONUS");
        slip.GrossPay.ShouldBe(1000m);
        slip.TotalDeductions.ShouldBe(100m);
    }

    [Fact]
    public async Task OneTimeInput_SetTwice_OverwritesRatherThanAccumulates()
    {
        var (_, employeeId, bonusId, _) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run))).Succeeded.ShouldBeTrue();

        (await SetInputAsync(run, employeeId, bonusId, 5000m)).Succeeded.ShouldBeTrue();
        (await SetInputAsync(run, employeeId, bonusId, 1200m, "corrected")).Succeeded.ShouldBeTrue();

        (await InScopeAsync<IPayRunService, Result<List<PayRunInputDto>>>(s => s.GetInputsAsync(run))).Data!.Count.ShouldBe(1);
        (await SinglePayslipAsync(run)).GrossPay.ShouldBe(2200m);
    }

    // ---------- ③ 录了没人读 = 录入那一刻 400 ----------

    [Fact]
    public async Task OneTimeInput_IsRejected_WhenTheComponentFormulaNeverReadsIt()
    {
        var (_, employeeId, _, taxId) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();

        // TAX 的公式是 GROSS * 0.10，永远不会读这笔钱。
        var result = await SetInputAsync(run, employeeId, taxId, 150m);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("Input()");
    }

    [Fact]
    public async Task OneTimeInput_IsRejected_WhenTheComponentIsNotOnTheEmployeeStructure()
    {
        var (_, employeeId, _, _) = await BonusScenarioAsync();
        var orphan = await ComponentWithAccountsAsync("SEVERANCE", SalaryComponentType.Earning, "Input()", expenseAccountCode: "5300");
        var run = await CreateJuneRunAsync();

        var result = await SetInputAsync(run, employeeId, orphan, 900m);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task OneTimeInput_IsRejected_WhenTheStructureLinePinsAFixedAmount()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var bonus = await ComponentWithAccountsAsync("BONUS", SalaryComponentType.Earning, "Input()", expenseAccountCode: "5300");

        // 行上钉死金额优先于公式 —— 录进去的输入会被它盖掉。
        var structure = await CreateStructureAsync("Pinned",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = bonus, Sequence = 2, AmountOverride = 300m });
        structure.Succeeded.ShouldBeTrue(structure.Message);

        var employee = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(employee.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));
        var run = await CreateJuneRunAsync();

        var result = await SetInputAsync(run, employee.Id, bonus, 5000m);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task OneTimeInput_RejectsANegativeAmount_OnAMonetaryComponent()
    {
        var (_, employeeId, bonusId, _) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();

        var result = await SetInputAsync(run, employeeId, bonusId, -100m);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task OneTimeInput_IsRejected_OnceTheRunIsPosted()
    {
        var (_, employeeId, bonusId, _) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run))).Succeeded.ShouldBeTrue();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(run))).Succeeded.ShouldBeTrue();

        var result = await SetInputAsync(run, employeeId, bonusId, 5000m);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
    }

    // ---------- 运行时扩展的组件目录 ----------

    [Fact]
    public async Task OneTimeInput_WorksForAComponentAddedToTheCatalogueAtRunTime()
    {
        var (structureId, employeeId, bonusId, taxId) = await BonusScenarioAsync();

        // 管理员现在新建一个扣款种类。它的输入名字没有在任何静态集合里声明过 ——
        // 因为 Input() 绑定的是"当前这一行的组件"，压根不存在第二个名字空间。
        var garnishment = await ComponentWithAccountsAsync("GARNISH", SalaryComponentType.Deduction, "Input()",
            liabilityAccountCode: "2200", condition: "Input() > 0");

        var basicId = (await InScopeAsync<ISalaryStructureService, Result<SalaryStructureDto>>(s => s.GetAsync(structureId)))
            .Data!.Lines.Single(l => l.ComponentCode == "BASIC").ComponentId;

        var updated = await InScopeAsync<ISalaryStructureService, Result<SalaryStructureDto>>(s => s.UpdateAsync(structureId,
            new UpdateSalaryStructureDto
            {
                Name = "Bonus",
                Frequency = PayFrequency.Monthly,
                Lines =
                [
                    new SalaryStructureLineInputDto { ComponentId = basicId, Sequence = 1 },
                    new SalaryStructureLineInputDto { ComponentId = bonusId, Sequence = 2 },
                    new SalaryStructureLineInputDto { ComponentId = taxId, Sequence = 3 },
                    new SalaryStructureLineInputDto { ComponentId = garnishment, Sequence = 4 }
                ]
            }));
        updated.Succeeded.ShouldBeTrue(updated.Message);

        var run = await CreateJuneRunAsync();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run))).Succeeded.ShouldBeTrue();
        (await SetInputAsync(run, employeeId, garnishment, 150m, "file 2026-0042")).Succeeded.ShouldBeTrue();

        var slip = await SinglePayslipAsync(run);
        slip.Lines.Single(l => l.ComponentCode == "GARNISH").Amount.ShouldBe(150m);
        slip.TotalDeductions.ShouldBe(250m);  // 税 100 + 罚扣 150
        slip.NetPay.ShouldBe(750m);
    }

    // ---------- 落库与重算的原子性 ----------

    [Fact]
    public async Task OneTimeInput_IsNotLeftBehind_WhenTheRecalculationFails()
    {
        var (_, employeeId, bonusId, _) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run))).Succeeded.ShouldBeTrue();

        // 从这里起重算必然失败（现实中是并发 409，走同一条返回路径）。
        _maxEmployeesPerRun = 0;

        var set = await SetInputAsync(run, employeeId, bonusId, 5000m);
        set.Succeeded.ShouldBeFalse();

        // ★输入不能留下来。留下来 = 一张"状态 Calculated、数字却不含这 5,000"的批次，
        // 而它照样可以过账 —— 正是这个功能从头到尾在防的那种失效。
        (await CountAsync<PayRunInput>(_ => true)).ShouldBe(0);

        _maxEmployeesPerRun = 1000;
        (await SinglePayslipAsync(run)).GrossPay.ShouldBe(1000m);
    }

    [Fact]
    public async Task DeletingAOneTimeInput_IsNotAppliedAlone_WhenTheRecalculationFails()
    {
        var (_, employeeId, bonusId, _) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run))).Succeeded.ShouldBeTrue();
        var set = await SetInputAsync(run, employeeId, bonusId, 5000m);
        set.Succeeded.ShouldBeTrue(set.Message);

        _maxEmployeesPerRun = 0;

        var deleted = await InScopeAsync<IPayRunService, Result>(s => s.DeleteInputAsync(run, set.Data!.Id));
        deleted.Succeeded.ShouldBeFalse();

        // 删除也要跟着回滚：否则工资单还带着 5,000，输入却已经不在了。
        (await CountAsync<PayRunInput>(_ => true)).ShouldBe(1);

        _maxEmployeesPerRun = 1000;
        (await SinglePayslipAsync(run)).GrossPay.ShouldBe(6000m);
    }

    // ---------- 出口再核：录入之后结构变了 ----------

    [Fact]
    public async Task OneTimeInput_ThatNobodyReadsAnyMore_BlocksThePayRunInsteadOfVanishing()
    {
        var (structureId, employeeId, bonusId, taxId) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();
        (await SetInputAsync(run, employeeId, bonusId, 5000m, "approved")).Succeeded.ShouldBeTrue();

        // 录入端放行过了。现在有人把奖金行从结构里拿掉——那笔已批准的 5,000 成了孤儿。
        var basicId = (await InScopeAsync<ISalaryStructureService, Result<SalaryStructureDto>>(s => s.GetAsync(structureId)))
            .Data!.Lines.Single(l => l.ComponentCode == "BASIC").ComponentId;
        var updated = await InScopeAsync<ISalaryStructureService, Result<SalaryStructureDto>>(s => s.UpdateAsync(structureId,
            new UpdateSalaryStructureDto
            {
                Name = "Bonus",
                Frequency = PayFrequency.Monthly,
                Lines =
                [
                    new SalaryStructureLineInputDto { ComponentId = basicId, Sequence = 1 },
                    new SalaryStructureLineInputDto { ComponentId = taxId, Sequence = 2 }
                ]
            }));
        updated.Succeeded.ShouldBeTrue(updated.Message);

        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run))).Succeeded.ShouldBeTrue();

        // ★ 不是"少发 5,000 然后一切正常"——错误落在工资单上，批次因此过不了账。
        var slip = await SinglePayslipAsync(run);
        slip.CalculationError.ShouldNotBeNull();
        slip.CalculationError!.ShouldContain("BONUS");

        var posted = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(run));
        posted.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task OneTimeInput_ThatNobodyReadsAnyMore_IsReportedWhenTheFormulaStopsCallingInput()
    {
        var (_, employeeId, bonusId, _) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();
        (await SetInputAsync(run, employeeId, bonusId, 5000m)).Succeeded.ShouldBeTrue();

        // 组件还在结构里，但公式与条件都改成了不读 Input() 的常量。
        var component = (await InScopeAsync<ISalaryComponentService, Result<SalaryComponentDto>>(s => s.GetAsync(bonusId))).Data!;
        var changed = await InScopeAsync<ISalaryComponentService, Result<SalaryComponentDto>>(s => s.UpdateAsync(bonusId,
            new UpdateSalaryComponentDto
            {
                Code = component.Code,
                Name = component.Name,
                Type = component.Type,
                Formula = "0",
                Condition = null,
                IsActive = true,
                ExpenseAccountId = component.ExpenseAccountId,
                LiabilityAccountId = component.LiabilityAccountId
            }));
        changed.Succeeded.ShouldBeTrue(changed.Message);

        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run))).Succeeded.ShouldBeTrue();

        var slip = await SinglePayslipAsync(run);
        slip.CalculationError.ShouldNotBeNull();
        slip.CalculationError!.ShouldContain("Input()");
    }

    [Fact]
    public async Task OneTimeInput_ThatIsStillRead_LeavesNoCalculationError()
    {
        var (_, employeeId, bonusId, _) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();
        (await SetInputAsync(run, employeeId, bonusId, 5000m)).Succeeded.ShouldBeTrue();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run))).Succeeded.ShouldBeTrue();

        (await SinglePayslipAsync(run)).CalculationError.ShouldBeNull();
    }

    [Fact]
    public async Task DeletingADraftRun_TakesItsInputsWithIt()
    {
        var (_, employeeId, bonusId, _) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();
        (await SetInputAsync(run, employeeId, bonusId, 400m)).Succeeded.ShouldBeTrue();

        var deleted = await InScopeAsync<IPayRunService, Result>(s => s.DeleteAsync(run));
        deleted.Succeeded.ShouldBeTrue(deleted.Message);
        (await CountAsync<PayRunInput>(_ => true)).ShouldBe(0);
    }
}
