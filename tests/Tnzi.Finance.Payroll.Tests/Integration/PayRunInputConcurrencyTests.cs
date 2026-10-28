namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 一次性输入的录入/删除 × 并发的整批计算。
/// </summary>
/// <remarks>
/// 计算先读全部输入、再在自己的事务里落 Calculated。草稿态的录入此前不碰批次行，
/// 所以批次的并发戳管不到它：计算读完输入之后才提交的一笔奖金，会被一张不含它的工资单
/// 「算完」，两边都返回成功，批次照常过账。删除方向反过来：删掉的奖金照发。
/// 这里用计算钩子把另一个请求的录入/删除精确地插进「读完输入」与「提交」之间。
/// </remarks>
public class PayRunInputConcurrencyTests : PayrollIntegrationTestBase
{
    private readonly InterleavingHook _hook = new();

    protected override void ConfigureExtraServices(IServiceCollection services)
    {
        services.AddSingleton<IPayslipCalculationHook>(_hook);
    }

    /// <summary>BASIC(Earning=BASE) + BONUS(Earning=Input()，仅在录入时出行) + TAX(Deduction=GROSS×10%)。</summary>
    private async Task<(Guid EmployeeId, Guid BonusId)> BonusScenarioAsync()
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
        return (employee.Id, bonus);
    }

    private Task<Guid> CreateJuneRunAsync()
        => CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 6, 30));

    private Task<Result<PayRunInputDto>> SetBonusAsync(Guid runId, Guid employeeId, Guid bonusId, decimal amount)
        => InScopeAsync<IPayRunService, Result<PayRunInputDto>>(s => s.SetInputAsync(runId, new SetPayRunInputDto
        {
            EmployeeId = employeeId,
            ComponentId = bonusId,
            Amount = amount
        }));

    private async Task<PayslipDto?> SinglePayslipOrNullAsync(Guid runId)
    {
        var list = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(runId));
        list.Succeeded.ShouldBeTrue(list.Message);
        if (list.Data!.Count == 0)
            return null;
        var slip = await InScopeAsync<IPayRunService, Result<PayslipDto>>(s => s.GetPayslipAsync(runId, list.Data!.Single().Id));
        slip.Succeeded.ShouldBeTrue(slip.Message);
        return slip.Data!;
    }

    [Fact]
    public async Task InputSavedWhileADraftRunIsCalculating_NeverLeavesACalculatedRunWithoutIt()
    {
        var (employeeId, bonusId) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();

        Result<PayRunInputDto>? interleaved = null;
        _hook.InterleaveOnce(async () => interleaved = await SetBonusAsync(run, employeeId, bonusId, 5000m));

        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run));

        interleaved.ShouldNotBeNull("the hook must have run the concurrent input write");
        interleaved.Succeeded.ShouldBeTrue(interleaved.Message);

        // 输入已经落库。那么批次要么是「计算被挡回（409）、还停在草稿」，要么工资单里有这笔奖金；
        // 「Calculated 但不含它」是唯一不可接受的结果。
        var reloaded = await ReloadAsync<PayRun>(run);
        if (reloaded!.Status == PayRunStatus.Calculated)
        {
            var slip = await SinglePayslipOrNullAsync(run);
            slip.ShouldNotBeNull();
            slip.Lines.ShouldContain(l => l.ComponentCode == "BONUS" && l.Amount == 5000m);
        }
        else
        {
            calc.Succeeded.ShouldBeFalse();
            calc.Code.ShouldBe(409);
            reloaded.Status.ShouldBe(PayRunStatus.Draft);
        }

        // 并发的那次计算被挡回后，再算一次就带上了奖金。
        var retry = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run));
        retry.Succeeded.ShouldBeTrue(retry.Message);
        (await SinglePayslipOrNullAsync(run))!.Lines.ShouldContain(l => l.ComponentCode == "BONUS" && l.Amount == 5000m);
    }

    [Fact]
    public async Task InputDeletedWhileADraftRunIsCalculating_NeverLeavesACalculatedRunThatStillPaysIt()
    {
        var (employeeId, bonusId) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();
        var set = await SetBonusAsync(run, employeeId, bonusId, 5000m);
        set.Succeeded.ShouldBeTrue(set.Message);

        Result? interleaved = null;
        _hook.InterleaveOnce(async () =>
            interleaved = await InScopeAsync<IPayRunService, Result>(s => s.DeleteInputAsync(run, set.Data!.Id)));

        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run));

        interleaved.ShouldNotBeNull("the hook must have run the concurrent delete");
        interleaved.Succeeded.ShouldBeTrue(interleaved.Message);

        var reloaded = await ReloadAsync<PayRun>(run);
        if (reloaded!.Status == PayRunStatus.Calculated)
        {
            var slip = await SinglePayslipOrNullAsync(run);
            slip.ShouldNotBeNull();
            slip.Lines.ShouldNotContain(l => l.ComponentCode == "BONUS");
        }
        else
        {
            calc.Succeeded.ShouldBeFalse();
            calc.Code.ShouldBe(409);
            reloaded.Status.ShouldBe(PayRunStatus.Draft);
        }
    }

    [Fact]
    public async Task InputSavedOnADraftRun_StillSucceedsWithoutAConcurrentCalculation()
    {
        var (employeeId, bonusId) = await BonusScenarioAsync();
        var run = await CreateJuneRunAsync();

        var first = await SetBonusAsync(run, employeeId, bonusId, 5000m);
        first.Succeeded.ShouldBeTrue(first.Message);
        // 第二次写同一个批次：上一次轮换过的并发戳不能让下一次误报冲突。
        var second = await SetBonusAsync(run, employeeId, bonusId, 6000m);
        second.Succeeded.ShouldBeTrue(second.Message);
        var deleted = await InScopeAsync<IPayRunService, Result>(s => s.DeleteInputAsync(run, second.Data!.Id));
        deleted.Succeeded.ShouldBeTrue(deleted.Message);

        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run));
        calc.Succeeded.ShouldBeTrue(calc.Message);
        (await SinglePayslipOrNullAsync(run))!.Lines.ShouldNotContain(l => l.ComponentCode == "BONUS");
    }

    /// <summary>在第一张工资单求值前，跑一次「另一个请求」（只跑一次）。</summary>
    private sealed class InterleavingHook : IPayslipCalculationHook
    {
        private Func<Task>? _pending;

        public void InterleaveOnce(Func<Task> action) => _pending = action;

        public async Task<Result> BeforeCalculateAsync(PayslipCalculationContext context, CancellationToken cancellationToken = default)
        {
            var action = Interlocked.Exchange(ref _pending, null);
            if (action != null)
                await action();
            return Result.Success();
        }
    }
}
