namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 组件编码与计算钩子声明的外部变量撞名，在钩子<b>后来</b>才声明这个名字时同样被拦住。
/// </summary>
/// <remarks>
/// 组件保存时会拒绝与已声明变量同名的编码，但钩子是会升级的：国家包或消费方的钩子新声明了一个
/// 与既有组件同名的变量，既有组件不会被重新保存。撞名是静默错账：钩子先注入标量，组件行算完把同名键
/// 覆盖成本行金额，读这个名字的公式拿到哪个值取决于它排在第几行。所以结构保存要拒绝，计算端要落
/// <c>CalculationError</c>（批次因此过不了账）。
/// </remarks>
public class HookVariableCollisionAfterUpgradeTests : PayrollIntegrationTestBase
{
    private const string LateVariable = "LATE_RATE";
    private readonly UpgradableHook _hook = new();

    protected override void ConfigureExtraServices(IServiceCollection services)
        => services.AddSingleton<IPayslipCalculationHook>(_hook);

    /// <summary>升级前：组件、结构、分配都已存在且合规。返回结构 Id 与组件 Id。</summary>
    private async Task<(Guid StructureId, Guid BasicId, Guid LateId)> BeforeTheUpgradeAsync()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var late = await ComponentWithAccountsAsync(LateVariable, SalaryComponentType.Deduction, "GROSS * 0.01", liabilityAccountCode: "2200");
        var structure = await CreateStructureAsync("Standard",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = late, Sequence = 2 });
        structure.Succeeded.ShouldBeTrue(structure.Message);

        var employee = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(employee.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));
        return (structure.Data!.Id, basic, late);
    }

    [Fact]
    public async Task ResavingTheStructure_AfterAHookStartsProvidingTheSameName_Rejected400()
    {
        var (structureId, basic, late) = await BeforeTheUpgradeAsync();
        _hook.Provided = [LateVariable];

        var result = await InScopeAsync<ISalaryStructureService, Result<SalaryStructureDto>>(s => s.UpdateAsync(structureId, new UpdateSalaryStructureDto
        {
            Name = "Standard",
            Frequency = PayFrequency.Monthly,
            Lines =
            [
                new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
                new SalaryStructureLineInputDto { ComponentId = late, Sequence = 2 },
            ],
        }));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain(LateVariable);
    }

    [Fact]
    public async Task Calculating_AfterAHookStartsProvidingTheSameName_FlagsThePayslip_AndBlocksPosting()
    {
        await BeforeTheUpgradeAsync();
        _hook.Provided = [LateVariable];
        var runId = await CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 7, 5));

        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId));

        calc.Succeeded.ShouldBeTrue(calc.Message);
        calc.Data!.ErrorCount.ShouldBe(1);
        var payslip = (await InScopeAsync<IRepository<Payslip, Guid>, List<Payslip>>(r => r.ToListAsync(p => p.PayRunId == runId))).Single();
        payslip.CalculationError!.ShouldContain(LateVariable);

        var posted = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(runId));
        posted.Succeeded.ShouldBeFalse();
    }

    /// <summary>对照：钩子没声明这个名字时，同一套数据照常算完、没有错误。</summary>
    [Fact]
    public async Task Calculating_WithoutTheCollision_HasNoErrors()
    {
        await BeforeTheUpgradeAsync();
        var runId = await CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 7, 5));

        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId));

        calc.Succeeded.ShouldBeTrue(calc.Message);
        calc.Data!.ErrorCount.ShouldBe(0);
    }

    /// <summary>可在测试中途「升级」的钩子：一开始什么都不声明。</summary>
    private sealed class UpgradableHook : IPayslipCalculationHook
    {
        public IReadOnlyCollection<string> Provided { get; set; } = [];

        public IReadOnlyCollection<string> ProvidedVariables => Provided;

        public Task<Result> BeforeCalculateAsync(PayslipCalculationContext context, CancellationToken cancellationToken = default)
        {
            foreach (var name in Provided)
                context.Variables[name] = 0.0595m;
            return Task.FromResult(Result.Success());
        }
    }
}
