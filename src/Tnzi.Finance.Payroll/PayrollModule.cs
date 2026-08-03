namespace Tnzi.Finance.Payroll;

/// <summary>
/// 薪酬子模块
/// 提供员工主数据、薪资组件与安全公式引擎、税级表原语、薪资结构与分配
/// 配置路径：Finance:Payroll
/// </summary>
/// <remarks>
/// Finance 的硬依赖子模块（非 AI 的 NoOp 回退形态）：只消费 Finance 的公共扩展面
/// （<see cref="Tnzi.Finance.Services.ILedgerPostingService"/> /
/// <see cref="Tnzi.Finance.Services.IVendorService"/> 等），
/// 不触碰其 Services/Internal。税级表只内置结构，内容由 country pack 播种或
/// 管理员手录——框架永不内置税表数值。
/// </remarks>
[DependsOn(typeof(FinanceModule))]
public class PayrollModule : TnziApplicationModule
{
    /// <summary>
    /// 加载顺序（在 FinanceModule(55) 之后）
    /// </summary>
    public override int LoadOrder => 56;

    /// <summary>
    /// 表名前缀（对齐 AI 子模块先例共享父前缀：Finance_Employee、Finance_SalaryComponent ...）
    /// </summary>
    public override string? TableNamePrefix => "Finance";

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddTnziOptions<PayrollOptions, PayrollOptionsValidator>(context.Configuration);
        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Code-declared permissions for this module's admin surfaces - the
        // Authorization module's PermissionDbSeeder picks every registered
        // provider up on startup (no-op when Authorization is not loaded).
        context.Services.AddTransient<IPermissionDefinitionProvider, PayrollPermissions>();

        // 主数据与配置
        context.Services.AddScoped<IEmployeeService, EmployeeService>();
        context.Services.AddScoped<ISalaryComponentService, SalaryComponentService>();
        context.Services.AddScoped<ISalaryStructureService, SalaryStructureService>();
        context.Services.AddScoped<IBracketTableService, BracketTableService>();

        // 公式引擎（NCalc 封装；scoped——经 IOptionsSnapshot 热读长度上限）
        context.Services.AddScoped<ISalaryFormulaEvaluator, NCalcSalaryFormulaEvaluator>();

        // 发薪批次全周期（过账/付款/作废经 Finance ILedgerPostingService 扩展面）
        context.Services.AddScoped<PayslipCalculator>();
        context.Services.AddScoped<PayrollPostingHelper>();
        context.Services.AddScoped<IPayRunService, PayRunService>();

        // Country pack（框架不内置 pack；消费方注册的 IPayrollCountryPack 经 IEnumerable 收集）
        context.Services.AddScoped<ICountryPackService, CountryPackService>();

        return Task.CompletedTask;
    }
}
