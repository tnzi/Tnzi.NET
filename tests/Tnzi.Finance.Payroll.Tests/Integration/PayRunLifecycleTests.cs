namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 发薪批次全周期：计算（条件门/Error 不炸批/圈选）、过账（借贷恒等式/科目聚合/员工维度/号分配/
/// 有 Error 拒绝/失败零残留）、付款（部分/累进/资金校验/状态机）、作废（全冲销归零）。
/// </summary>
public class PayRunLifecycleTests : PayrollIntegrationTestBase
{
    private static readonly DateTime PeriodStart = new(2026, 6, 1);
    private static readonly DateTime PeriodEnd = new(2026, 6, 30);
    private static readonly DateTime PayDate = new(2026, 7, 5);

    private Task<Result<PayRunDto>> CalculateAsync(Guid runId)
        => InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId));

    private Task<Result<PayRunDto>> PostAsync(Guid runId)
        => InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(runId));

    // ── 计算 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Calculate_ProducesPayslips_WithCorrectTotals()
    {
        await StandardScenarioAsync();
        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);

        var result = await CalculateAsync(runId);
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Status.ShouldBe(PayRunStatus.Calculated);
        result.Data.EmployeeCount.ShouldBe(1);
        result.Data.GrossTotal.ShouldBe(1000m);
        result.Data.DeductionTotal.ShouldBe(100m);
        result.Data.EmployerCostTotal.ShouldBe(50m);
        result.Data.NetTotal.ShouldBe(900m);
        result.Data.ErrorCount.ShouldBe(0);
    }

    [Fact]
    public async Task Calculate_ConditionGate_SkipsLine()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        // 仅当 BASE > 5000 才发放的奖金；base=1000 时条件为假 → 该行被跳过
        var bonus = await ComponentWithAccountsAsync("BONUS", SalaryComponentType.Earning, "500", expenseAccountCode: "5300", condition: "BASE > 5000");
        var structure = await CreateStructureAsync("Cond",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = bonus, Sequence = 2 });
        structure.Succeeded.ShouldBeTrue(structure.Message);
        var emp = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(emp.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        var result = await CalculateAsync(runId);
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.GrossTotal.ShouldBe(1000m); // 奖金未计入

        var slips = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(runId));
        var slip = await InScopeAsync<IPayRunService, Result<PayslipDto>>(s => s.GetPayslipAsync(runId, slips.Data!.Single().Id));
        slip.Data!.Lines.Count.ShouldBe(1); // 仅 BASIC，BONUS 被条件跳过
    }

    [Fact]
    public async Task Calculate_NegativeNet_RecordsErrorWithoutThrowing()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        // 扣减 = 2×毛收入 → 净额为负
        var huge = await ComponentWithAccountsAsync("HUGE", SalaryComponentType.Deduction, "GROSS * 2", liabilityAccountCode: "2200");
        var structure = await CreateStructureAsync("Neg",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = huge, Sequence = 2 });
        structure.Succeeded.ShouldBeTrue(structure.Message);
        var emp = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(emp.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        var result = await CalculateAsync(runId);
        result.Succeeded.ShouldBeTrue(result.Message); // 计算成功，错误记在 slip 上
        result.Data!.ErrorCount.ShouldBe(1);

        var post = await PostAsync(runId);
        post.Succeeded.ShouldBeFalse(); // 有 Error 禁止过账
        post.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Calculate_ErrorSlip_DoesNotBlockOtherSlips()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var huge = await ComponentWithAccountsAsync("HUGE", SalaryComponentType.Deduction, "GROSS * 2", liabilityAccountCode: "2200");
        var structure = await CreateStructureAsync("Mixed",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = huge, Sequence = 2 });
        structure.Succeeded.ShouldBeTrue(structure.Message);

        var ok = await CreateEmployeeAsync("OK", "Fine");
        var bad = await CreateEmployeeAsync("BAD", "Broken");
        // OK 员工用一个不炸的结构；BAD 员工用负净额结构
        var okStructure = await CreateStructureAsync("OkOnly",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 });
        okStructure.Succeeded.ShouldBeTrue(okStructure.Message);
        await AssignAsync(ok.Id, okStructure.Data!.Id, 1000m, new DateTime(2026, 1, 1));
        await AssignAsync(bad.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        var result = await CalculateAsync(runId);
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.EmployeeCount.ShouldBe(2);
        result.Data.ErrorCount.ShouldBe(1); // 仅 BAD 出错，OK 正常
    }

    [Fact]
    public async Task Calculate_NoEligibleEmployees_Fails()
    {
        await StandardScenarioAsync();
        // 期间在分配生效日之前（2026-01-01），仍圈选到（EffectiveFrom <= PeriodEnd）——改用未来 termination 场景
        var runId = await CreateRunAsync(new DateTime(2025, 1, 1), new DateTime(2025, 1, 31), new DateTime(2025, 2, 5));
        var result = await CalculateAsync(runId);
        result.Succeeded.ShouldBeFalse(); // 分配生效日 2026-01-01 晚于 2025 期末 → 无人圈选
        result.Code.ShouldBe(400);
    }

    // ── 过账 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_BalancedGL_AggregatesByAccount_WithEmployeeDimension()
    {
        await StandardScenarioAsync();
        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        (await CalculateAsync(runId)).Succeeded.ShouldBeTrue();

        var post = await PostAsync(runId);
        post.Succeeded.ShouldBeTrue(post.Message);
        post.Data!.Status.ShouldBe(PayRunStatus.Posted);
        post.Data.Number.ShouldNotBeNull();
        post.Data.Number!.ShouldStartWith("PR-");

        var slips = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(runId));
        var payslip = await InScopeAsync<IPayRunService, Result<PayslipDto>>(s => s.GetPayslipAsync(runId, slips.Data!.Single().Id));
        payslip.Data!.JournalEntryId.ShouldNotBeNull();

        // 借贷恒等式
        var (debit, credit) = await JournalTotalsAsync(payslip.Data.JournalEntryId!.Value);
        debit.ShouldBe(credit);
        debit.ShouldBe(1050m); // 5300 = BASIC 1000 + PENSION 50

        // 5300 费用科目聚合为单行（BASIC + PENSION）
        var expenseId = await AccountIdByCodeAsync("5300");
        using var scope = ServiceProvider.CreateScope();
        var lineRepo = scope.ServiceProvider.GetRequiredService<IRepository<JournalLine, Guid>>();
        var lines = await lineRepo.ToListAsync(l => l.JournalEntryId == payslip.Data.JournalEntryId!.Value);
        lines.Count(l => l.AccountId == expenseId).ShouldBe(1);
        lines.Single(l => l.AccountId == expenseId).Debit.ShouldBe(1050m);

        // WagesPayable 按员工维度
        var wagesId = await AccountIdByCodeAsync("2400");
        var wagesLine = lines.Single(l => l.AccountId == wagesId);
        wagesLine.Credit.ShouldBe(900m);
        wagesLine.PartyType.ShouldBe("Employee");
        wagesLine.PartyId.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Post_AssignsSequentialNumber()
    {
        await StandardScenarioAsync();
        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        (await CalculateAsync(runId)).Succeeded.ShouldBeTrue();
        var post = await PostAsync(runId);
        post.Succeeded.ShouldBeTrue(post.Message);
        post.Data!.Number.ShouldBe("PR-000001");
    }

    [Fact]
    public async Task Post_WithoutWagesPayableRole_FailsAndLeavesNoResidue()
    {
        // 不 seed COA → 无 WagesPayable 角色（但组件科目需存在，故手建费用/负债科目 + 手建组件）
        await SeedCoaAsync();
        // 清除 2400 的 WagesPayable 角色，模拟未配置——挂角色的科目受删除守卫保护（409），
        // 释放路径只有先清角色，这里直接清角色即可让角色解析落空
        var released = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(async s =>
        {
            var acc = await s.FindByCodeAsync("2400");
            return await s.UpdateAsync(acc!.Id, new UpdateAccountDto
            {
                Code = acc.Code,
                Name = acc.Name,
                Description = acc.Description,
                SubType = acc.SubType,
                ParentId = acc.ParentId,
                Currency = acc.Currency,
                SystemRole = null,
                CashFlowActivity = acc.CashFlowActivity,
                IsActive = acc.IsActive
            });
        });
        released.Succeeded.ShouldBeTrue(released.Message);

        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var structure = await CreateStructureAsync("Simple", new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 });
        structure.Succeeded.ShouldBeTrue(structure.Message);
        var emp = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(emp.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        (await CalculateAsync(runId)).Succeeded.ShouldBeTrue();

        var post = await PostAsync(runId);
        post.Succeeded.ShouldBeFalse();
        post.Code.ShouldBe(400);

        // 零残留：run 仍是 Calculated、无号、无凭证
        var run = await ReloadAsync<PayRun>(runId);
        run!.Status.ShouldBe(PayRunStatus.Calculated);
        run.Number.ShouldBeNull();

        using var scope = ServiceProvider.CreateScope();
        var entryRepo = scope.ServiceProvider.GetRequiredService<IRepository<JournalEntry, Guid>>();
        var entries = await entryRepo.ToListAsync(e => e.SourceType == "PayRun" && e.SourceId == runId.ToString());
        entries.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Post_Twice_SecondRejected()
    {
        await StandardScenarioAsync();
        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        (await CalculateAsync(runId)).Succeeded.ShouldBeTrue();
        (await PostAsync(runId)).Succeeded.ShouldBeTrue();

        var second = await PostAsync(runId);
        second.Succeeded.ShouldBeFalse();
        second.Code.ShouldBe(409);
    }

    // ── 付款 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pay_All_PostsPaymentAndMarksPaid()
    {
        await StandardScenarioAsync();
        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        (await CalculateAsync(runId)).Succeeded.ShouldBeTrue();
        (await PostAsync(runId)).Succeeded.ShouldBeTrue();

        var bankId = await AccountIdByCodeAsync("1120");
        var pay = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PayAsync(runId, new PayRunPaymentDto
        {
            PaymentAccountId = bankId,
            PaymentDate = PayDate,
            PaymentMethod = "BankTransfer"
        }));
        pay.Succeeded.ShouldBeTrue(pay.Message);
        pay.Data!.Status.ShouldBe(PayRunStatus.Paid);

        var slips = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(runId));
        var slip = await InScopeAsync<IPayRunService, Result<PayslipDto>>(s => s.GetPayslipAsync(runId, slips.Data!.Single().Id));
        slip.Data!.PaymentStatus.ShouldBe(PayslipPaymentStatus.Paid);
        slip.Data.PaymentJournalEntryId.ShouldNotBeNull();

        var (debit, credit) = await JournalTotalsAsync(slip.Data.PaymentJournalEntryId!.Value);
        debit.ShouldBe(900m); // Dr WagesPayable 900
        credit.ShouldBe(900m); // Cr Bank 900
    }

    [Fact]
    public async Task Pay_Partial_ThenRemainder_TransitionsState()
    {
        await StandardScenarioAsync("A");
        var b = await CreateEmployeeAsync("B", "Bee");
        await AssignAsync(b.Id, (await FirstStructureIdAsync()), 2000m, new DateTime(2026, 1, 1));

        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        (await CalculateAsync(runId)).Succeeded.ShouldBeTrue();
        (await PostAsync(runId)).Succeeded.ShouldBeTrue();

        var slips = (await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(runId))).Data!;
        var first = slips.First(s => s.EmployeeCode == "A");
        var bankId = await AccountIdByCodeAsync("1120");

        var pay1 = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PayAsync(runId, new PayRunPaymentDto
        {
            EmployeeIds = new List<Guid> { first.EmployeeId },
            PaymentAccountId = bankId,
            PaymentDate = PayDate
        }));
        pay1.Succeeded.ShouldBeTrue(pay1.Message);
        pay1.Data!.Status.ShouldBe(PayRunStatus.PartiallyPaid);

        var pay2 = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PayAsync(runId, new PayRunPaymentDto
        {
            PaymentAccountId = bankId,
            PaymentDate = PayDate
        }));
        pay2.Succeeded.ShouldBeTrue(pay2.Message);
        pay2.Data!.Status.ShouldBe(PayRunStatus.Paid);
    }

    [Fact]
    public async Task Pay_NonCashAccount_Rejected()
    {
        await StandardScenarioAsync();
        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        (await CalculateAsync(runId)).Succeeded.ShouldBeTrue();
        (await PostAsync(runId)).Succeeded.ShouldBeTrue();

        var expenseId = await AccountIdByCodeAsync("5300"); // 非资金科目
        var pay = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PayAsync(runId, new PayRunPaymentDto
        {
            PaymentAccountId = expenseId,
            PaymentDate = PayDate
        }));
        pay.Succeeded.ShouldBeFalse();
        pay.Code.ShouldBe(400);
    }

    // ── 作废 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Void_ReversesPaymentAndPosting_LeavesGLNetZero()
    {
        await StandardScenarioAsync();
        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        (await CalculateAsync(runId)).Succeeded.ShouldBeTrue();
        (await PostAsync(runId)).Succeeded.ShouldBeTrue();
        var bankId = await AccountIdByCodeAsync("1120");
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PayAsync(runId, new PayRunPaymentDto
        {
            PaymentAccountId = bankId,
            PaymentDate = PayDate
        }))).Succeeded.ShouldBeTrue();

        var voidResult = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.VoidAsync(runId));
        voidResult.Succeeded.ShouldBeTrue(voidResult.Message);
        voidResult.Data!.Status.ShouldBe(PayRunStatus.Voided);

        // 全部凭证（过账 + 付款 + 各自冲销）净额归零
        using var scope = ServiceProvider.CreateScope();
        var lineRepo = scope.ServiceProvider.GetRequiredService<IRepository<JournalLine, Guid>>();
        var allLines = await lineRepo.ToListAsync(_ => true);
        allLines.Sum(l => l.Debit).ShouldBe(allLines.Sum(l => l.Credit));

        var wagesId = await AccountIdByCodeAsync("2400");
        var wagesNet = allLines.Where(l => l.AccountId == wagesId).Sum(l => l.Debit - l.Credit);
        wagesNet.ShouldBe(0m); // 过账贷 900 / 付款借 900 / 两笔冲销 → 净 0
    }

    [Fact]
    public async Task Void_DraftRun_Rejected()
    {
        await StandardScenarioAsync();
        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        (await CalculateAsync(runId)).Succeeded.ShouldBeTrue();

        var voidResult = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.VoidAsync(runId));
        voidResult.Succeeded.ShouldBeFalse(); // 未过账不可作废
        voidResult.Code.ShouldBe(409);
    }

    // ── 单张输入修正 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdatePayslipInputs_RecalculatesSingleSlip()
    {
        await SeedCoaAsync();
        // 按出勤天数比例发放：BASE * WORKED_DAYS / PERIOD_DAYS
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning,
            "round(BASE * WORKED_DAYS / PERIOD_DAYS, 2)", expenseAccountCode: "5300");
        var structure = await CreateStructureAsync("Prorated", new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 });
        structure.Succeeded.ShouldBeTrue(structure.Message);
        var emp = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(emp.Id, structure.Data!.Id, 3000m, new DateTime(2026, 1, 1));

        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate); // 30 天
        (await CalculateAsync(runId)).Succeeded.ShouldBeTrue();
        var slips = (await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(runId))).Data!;
        var slipId = slips.Single().Id;

        // 改为出勤 15 天 → 减半
        var updated = await InScopeAsync<IPayRunService, Result<PayslipDto>>(s => s.UpdatePayslipInputsAsync(runId, slipId, new UpdatePayslipInputsDto { WorkedDays = 15 }));
        updated.Succeeded.ShouldBeTrue(updated.Message);
        updated.Data!.GrossPay.ShouldBe(1500m);

        var run = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.GetAsync(runId));
        run.Data!.GrossTotal.ShouldBe(1500m); // 聚合快照同步
    }

    private async Task<Guid> FirstStructureIdAsync()
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<SalaryStructure, Guid>>();
        var structure = await repo.FirstOrDefaultAsync(s => s.Name == "Standard");
        return structure!.Id;
    }
}
