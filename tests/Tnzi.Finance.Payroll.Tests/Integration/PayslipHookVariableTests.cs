namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 钩子注入的运行时变量 seam：<see cref="IPayslipCalculationHook.BeforeCalculateAsync"/> 能往
/// <see cref="PayslipCalculationContext.Variables"/> 里注入按(辖区, 生效日)变化的标量
/// （CPP_RATE 之类），而结构保存期的静态校验必须让公式**引用得到**它们——否则这个扩展点
/// 对它设计出来要服务的场景（country pack）不可用。
/// <para>
/// 三条断言互为约束：①声明过的外部变量可保存可求值；②没人提供的变量名仍在保存期 400
/// （静态检查的价值不能因此丢掉）；③**注入了但没声明**的变量同样 400 —— 契约是声明，
/// 不是注入，否则「拼错变量名要报错」就退化成运行期才发现。
/// </para>
/// </summary>
public class PayslipHookVariableTests : PayrollIntegrationTestBase
{
    private const string DeclaredVariable = "CPP_RATE";
    private const string UndeclaredVariable = "UNDECLARED_RATE";
    private const decimal DeclaredValue = 0.0595m;

    protected override void ConfigureExtraServices(IServiceCollection services)
    {
        services.AddScoped<IPayslipCalculationHook, ScalarInjectingHook>();
    }

    [Fact]
    public async Task Structure_ReferencingHookProvidedVariable_SavesAndEvaluates()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var cpp = await ComponentWithAccountsAsync("CPP", SalaryComponentType.Deduction,
            $"round(GROSS * {DeclaredVariable}, 2)", liabilityAccountCode: "2200");

        var structure = await CreateStructureAsync("Statutory",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 10 },
            new SalaryStructureLineInputDto { ComponentId = cpp, Sequence = 20 });

        structure.Succeeded.ShouldBeTrue(structure.Message);

        var employee = await CreateEmployeeAsync("EMP1", "EMP1");
        await AssignAsync(employee.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));
        var runId = await CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 7, 5));

        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId));
        calc.Succeeded.ShouldBeTrue(calc.Message);
        calc.Data!.ErrorCount.ShouldBe(0);

        // 注入值真的进了公式：1000 × 0.0595 = 59.50（不是 0，也不是「未知变量」错误）
        var lines = await InScopeAsync<IRepository<PayslipLine, Guid>, List<PayslipLine>>(
            r => r.ToListAsync(l => l.ComponentCode == "CPP"));
        lines.Count.ShouldBe(1);
        lines[0].Amount.ShouldBe(59.50m);
    }

    [Fact]
    public async Task Structure_ReferencingNobodysVariable_StillRejected()
    {
        var mystery = await CreateComponentAsync("MYSTERY", SalaryComponentType.Deduction, formula: "NON_EXISTENT * 2");

        var result = await CreateStructureAsync("Mystery",
            new SalaryStructureLineInputDto { ComponentId = mystery.Id, Sequence = 10 });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldNotBeNull().ShouldContain("NON_EXISTENT");
    }

    [Fact]
    public async Task Structure_ReferencingInjectedButUndeclaredVariable_StillRejected()
    {
        // 钩子确实会在运行期注入它，但没有声明——保存期无从知道它存在，必须仍然 400。
        var sneaky = await CreateComponentAsync("SNEAKY", SalaryComponentType.Deduction,
            formula: $"GROSS * {UndeclaredVariable}");

        var result = await CreateStructureAsync("Sneaky",
            new SalaryStructureLineInputDto { ComponentId = sneaky.Id, Sequence = 10 });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldNotBeNull().ShouldContain(UndeclaredVariable);
    }

    [Fact]
    public async Task Component_CollidingWithHookProvidedVariable_Rejected()
    {
        // 撞名不挡住的话是静默错账：按序求值会用组件金额覆盖注入的费率，
        // 引用它的公式读到哪个值取决于行序。
        var result = await InScopeAsync<ISalaryComponentService, Result<SalaryComponentDto>>(
            s => s.CreateAsync(new CreateSalaryComponentDto
            {
                Code = DeclaredVariable,
                Name = "Collides with the hook's scalar",
                Type = SalaryComponentType.Deduction
            }));

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull().ShouldContain(DeclaredVariable);
    }

    /// <summary>
    /// 模拟 country pack 的标量注入钩子：注入两个变量，只声明其中一个。
    /// </summary>
    private sealed class ScalarInjectingHook : IPayslipCalculationHook
    {
        public IReadOnlyCollection<string> ProvidedVariables => [DeclaredVariable];

        public Task<Result> BeforeCalculateAsync(PayslipCalculationContext context, CancellationToken cancellationToken = default)
        {
            context.Variables[DeclaredVariable] = DeclaredValue;
            context.Variables[UndeclaredVariable] = 0.0123m;
            return Task.FromResult(Result.Success());
        }
    }
}
