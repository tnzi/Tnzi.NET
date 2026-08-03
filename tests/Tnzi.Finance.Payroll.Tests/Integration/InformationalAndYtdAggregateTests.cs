namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 两个由消费应用反推出来的能力：备注组件（<see cref="SalaryComponentType.Informational"/>）
/// 与 <c>Ytd()</c> 的按类型聚合键。
///
/// 两者的共同点是：没有它们时，能表达出来的**替代写法都会改动已经印出去的数字**
/// （把备注记成扣减 = 扣第二遍；把收入项在公式里逐个列全 = 漏一个就静默少扣上限）。
/// </summary>
public class InformationalAndYtdAggregateTests : PayrollIntegrationTestBase
{
    // ---------- 备注组件 ----------

    [Fact]
    public async Task Informational_LineIsOnThePayslip_ButMovesNoTotal()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var tax = await ComponentWithAccountsAsync("TAX", SalaryComponentType.Deduction, "GROSS * 0.10", liabilityAccountCode: "2200");
        // 无薪假天数折算额：只作说明，Gross 早已被扣减过了。
        var memo = await ComponentWithAccountsAsync("UNPAID_LEAVE", SalaryComponentType.Informational, "250");

        var structure = await CreateStructureAsync("Memo",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = tax, Sequence = 2 },
            new SalaryStructureLineInputDto { ComponentId = memo, Sequence = 3 });
        structure.Succeeded.ShouldBeTrue(structure.Message);

        var emp = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(emp.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        var run = await CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 6, 30));
        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run));
        calc.Succeeded.ShouldBeTrue(calc.Message);

        var list = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(run));
        var slip = await InScopeAsync<IPayRunService, Result<PayslipDto>>(s => s.GetPayslipAsync(run, list.Data!.Single().Id));
        slip.Succeeded.ShouldBeTrue(slip.Message);

        // 行在工资条上，金额也在。
        var memoLine = slip.Data!.Lines.Single(l => l.ComponentCode == "UNPAID_LEAVE");
        memoLine.Amount.ShouldBe(250m);

        // 但四个合计一个都没动：这正是它与 Deduction 的区别。
        slip.Data.GrossPay.ShouldBe(1000m);
        slip.Data.TotalDeductions.ShouldBe(100m);
        slip.Data.EmployerCost.ShouldBe(0m);
        slip.Data.NetPay.ShouldBe(900m);
    }

    [Fact]
    public async Task Informational_CanBeReferencedByLaterFormulas_AsANamedIntermediate()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        // 附加税之前的省税：具名之后，后面那行不必把整个子表达式再抄一遍。
        var preSurtax = await ComponentWithAccountsAsync("TAX_PRE_SURTAX", SalaryComponentType.Informational, "GROSS * 0.0505");
        var tax = await ComponentWithAccountsAsync("TAX", SalaryComponentType.Deduction,
            "TAX_PRE_SURTAX + max(0, TAX_PRE_SURTAX - 20) * 0.20", liabilityAccountCode: "2200");

        var structure = await CreateStructureAsync("Intermediate",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = preSurtax, Sequence = 2 },
            new SalaryStructureLineInputDto { ComponentId = tax, Sequence = 3 });
        structure.Succeeded.ShouldBeTrue(structure.Message);

        var emp = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(emp.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        var run = await CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 6, 30));
        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run));
        calc.Succeeded.ShouldBeTrue(calc.Message);

        var list = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(run));
        var slip = await InScopeAsync<IPayRunService, Result<PayslipDto>>(s => s.GetPayslipAsync(run, list.Data!.Single().Id));

        // 50.50 → 附加税 (50.50 − 20) × 0.20 = 6.10 → 合计 56.60
        slip.Data!.Lines.Single(l => l.ComponentCode == "TAX_PRE_SURTAX").Amount.ShouldBe(50.50m);
        slip.Data.TotalDeductions.ShouldBe(56.60m);
        // 中间量本身不进扣减：56.60 而不是 107.10。
        slip.Data.NetPay.ShouldBe(943.40m);
    }

    [Fact]
    public async Task Informational_PostsNothing_AndThereforeNeedsNoAccount()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        // 没有任何科目，而过账仍必须成功——若过账把它当收入/扣减处理，
        // 这里会因为"没有配置科目"而 400。
        var memo = await ComponentWithAccountsAsync("MEMO", SalaryComponentType.Informational, "42");

        var structure = await CreateStructureAsync("MemoPost",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = memo, Sequence = 2 });
        structure.Succeeded.ShouldBeTrue(structure.Message);

        var emp = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(emp.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        var run = await CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 6, 30));
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run))).Succeeded.ShouldBeTrue();

        var post = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(run));
        post.Succeeded.ShouldBeTrue(post.Message);
        // 分录只反映 1000 的工资，备注行没有留下任何一笔。
        post.Data!.GrossTotal.ShouldBe(1000m);
    }

    [Fact]
    public async Task Informational_MayBeNegative_WhileTheOtherTypesMayNot()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        // 抵免、冲回天然带符号；它进不了任何合计，所以产生不了荒谬的净额。
        var credit = await ComponentWithAccountsAsync("CREDIT", SalaryComponentType.Informational, "0 - 75");

        var structure = await CreateStructureAsync("SignedMemo",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = credit, Sequence = 2 });
        structure.Succeeded.ShouldBeTrue(structure.Message);

        var emp = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(emp.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        var run = await CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 6, 30));
        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run));
        calc.Succeeded.ShouldBeTrue(calc.Message);

        var list = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(run));
        var slip = await InScopeAsync<IPayRunService, Result<PayslipDto>>(s => s.GetPayslipAsync(run, list.Data!.Single().Id));
        slip.Data!.CalculationError.ShouldBeNull();
        slip.Data.Lines.Single(l => l.ComponentCode == "CREDIT").Amount.ShouldBe(-75m);
        slip.Data.NetPay.ShouldBe(1000m);
    }

    [Fact]
    public async Task Informational_RejectsAnAccount_RatherThanSilentlyIgnoringIt()
    {
        await SeedCoaAsync();
        var expenseId = await AccountIdByCodeAsync("5300");

        var result = await InScopeAsync<ISalaryComponentService, Result<SalaryComponentDto>>(s => s.CreateAsync(
            new CreateSalaryComponentDto
            {
                Code = "MEMO",
                Name = "Memo",
                Type = SalaryComponentType.Informational,
                Formula = "1",
                ExpenseAccountId = expenseId
            }));

        // 静默忽略一个明确填写的科目，比拒绝它更糟：填的人会以为账已经接好了。
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    // ---------- Ytd() 聚合键 ----------

    [Fact]
    public async Task YtdGross_AggregatesEveryEarning_WithoutListingThemInTheFormula()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var bonus = await ComponentWithAccountsAsync("BONUS", SalaryComponentType.Earning, "200", expenseAccountCode: "5300");
        var tax = await ComponentWithAccountsAsync("TAX", SalaryComponentType.Deduction, "GROSS * 0.10", liabilityAccountCode: "2200");
        // ★ 公式里一个收入项的名字都没有出现——明年加一个收入项也不会漏。
        var priorGross = await ComponentWithAccountsAsync("PRIOR_GROSS", SalaryComponentType.Informational, "Ytd('#GROSS')");

        var structure = await CreateStructureAsync("Agg",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = bonus, Sequence = 2 },
            new SalaryStructureLineInputDto { ComponentId = tax, Sequence = 3 },
            new SalaryStructureLineInputDto { ComponentId = priorGross, Sequence = 4 });
        structure.Succeeded.ShouldBeTrue(structure.Message);

        var emp = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(emp.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        // Run 1：gross = 1000 + 200 = 1200，扣税 120。
        var run1 = await CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 6, 30));
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run1))).Succeeded.ShouldBeTrue();
        var post1 = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(run1));
        post1.Succeeded.ShouldBeTrue(post1.Message);

        // Run 2：Ytd('#GROSS') 必须是 1200（两个收入项之和），不是 1000（只有 BASIC）。
        var run2 = await CreateRunAsync(new DateTime(2026, 7, 1), new DateTime(2026, 7, 31), new DateTime(2026, 7, 31));
        var calc2 = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run2));
        calc2.Succeeded.ShouldBeTrue(calc2.Message);

        var list = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(run2));
        var slip = await InScopeAsync<IPayRunService, Result<PayslipDto>>(s => s.GetPayslipAsync(run2, list.Data!.Single().Id));
        slip.Data!.Lines.Single(l => l.ComponentCode == "PRIOR_GROSS").Amount.ShouldBe(1200m);
    }

    [Fact]
    public async Task YtdAggregates_CoverDeductionsEmployerCostAndNet()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var tax = await ComponentWithAccountsAsync("TAX", SalaryComponentType.Deduction, "GROSS * 0.10", liabilityAccountCode: "2200");
        var pension = await ComponentWithAccountsAsync("PENSION", SalaryComponentType.EmployerContribution, "GROSS * 0.05",
            expenseAccountCode: "5300", liabilityAccountCode: "2200");
        var pd = await ComponentWithAccountsAsync("PRIOR_DED", SalaryComponentType.Informational, "Ytd('#DEDUCTIONS')");
        var pe = await ComponentWithAccountsAsync("PRIOR_EMP", SalaryComponentType.Informational, "Ytd('#EMPLOYER')");
        var pn = await ComponentWithAccountsAsync("PRIOR_NET", SalaryComponentType.Informational, "Ytd('#NET')");

        var structure = await CreateStructureAsync("AllAgg",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = tax, Sequence = 2 },
            new SalaryStructureLineInputDto { ComponentId = pension, Sequence = 3 },
            new SalaryStructureLineInputDto { ComponentId = pd, Sequence = 4 },
            new SalaryStructureLineInputDto { ComponentId = pe, Sequence = 5 },
            new SalaryStructureLineInputDto { ComponentId = pn, Sequence = 6 });
        structure.Succeeded.ShouldBeTrue(structure.Message);

        var emp = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(emp.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        var run1 = await CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 6, 30));
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run1))).Succeeded.ShouldBeTrue();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(run1))).Succeeded.ShouldBeTrue();

        var run2 = await CreateRunAsync(new DateTime(2026, 7, 1), new DateTime(2026, 7, 31), new DateTime(2026, 7, 31));
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run2))).Succeeded.ShouldBeTrue();

        var list = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(run2));
        var slip = await InScopeAsync<IPayRunService, Result<PayslipDto>>(s => s.GetPayslipAsync(run2, list.Data!.Single().Id));
        var lines = slip.Data!.Lines.ToDictionary(l => l.ComponentCode, l => l.Amount);

        lines["PRIOR_DED"].ShouldBe(100m);   // 1000 × 10%
        lines["PRIOR_EMP"].ShouldBe(50m);    // 1000 × 5%，且不混进扣减
        lines["PRIOR_NET"].ShouldBe(900m);   // 收入 − 扣减，雇主承担项不参与
    }

    [Fact]
    public async Task YtdAggregates_ExcludeInformationalLines()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var memo = await ComponentWithAccountsAsync("MEMO", SalaryComponentType.Informational, "500");
        var priorGross = await ComponentWithAccountsAsync("PRIOR_GROSS", SalaryComponentType.Informational, "Ytd('#GROSS')");

        var structure = await CreateStructureAsync("MemoAgg",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = memo, Sequence = 2 },
            new SalaryStructureLineInputDto { ComponentId = priorGross, Sequence = 3 });
        structure.Succeeded.ShouldBeTrue(structure.Message);

        var emp = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(emp.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        var run1 = await CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 6, 30));
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run1))).Succeeded.ShouldBeTrue();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(run1))).Succeeded.ShouldBeTrue();

        var run2 = await CreateRunAsync(new DateTime(2026, 7, 1), new DateTime(2026, 7, 31), new DateTime(2026, 7, 31));
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run2))).Succeeded.ShouldBeTrue();

        var list = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(run2));
        var slip = await InScopeAsync<IPayRunService, Result<PayslipDto>>(s => s.GetPayslipAsync(run2, list.Data!.Single().Id));

        // 备注行进不了任何合计——历史累计上也一样，否则"不动合计"就只是本期成立。
        slip.Data!.Lines.Single(l => l.ComponentCode == "PRIOR_GROSS").Amount.ShouldBe(1000m);
    }
}
