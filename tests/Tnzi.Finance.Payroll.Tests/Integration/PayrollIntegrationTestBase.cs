using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Moq;
using Tnzi.Domain.Entities;
using Tnzi.EventBus;
using Tnzi.Finance.Services.Internal;
using Tnzi.Mapster;

namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// Payroll 集成测试基类：真实 SQLite + 仓储 + UnitOfWork + 全部薪酬服务
/// （含影子供应商衔接所需的 VendorService，以及 P4c 过账/付款/作废所需的 Finance 总账栈），
/// 用于验证主数据 CRUD、公式静态校验、税级表版本解析、分配生效日解析与发薪批次全周期的端到端行为。
/// </summary>
public abstract class PayrollIntegrationTestBase : IntegratedTestBase<PayrollTestDbContext>
{
    protected PayrollIntegrationTestBase()
    {
        var config = new TypeAdapterConfig();
        MapperExtensions.SetMapper(new Mapper(config));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions();
        services.Configure<PayrollOptions>(_ => { });
        services.Configure<FinanceOptions>(o =>
        {
            o.BaseCurrency = "USD";
            o.JournalNumberPrefix = "JE-";
            o.JournalNumberPadding = 6;
        });
        services.AddSingleton(TimeProvider.System);

        // 仓储（IRepository + IReadOnlyRepository）
        AddRepo<Employee>(services);
        AddRepo<SalaryComponent>(services);
        AddRepo<SalaryStructure>(services);
        AddRepo<SalaryStructureLine>(services);
        AddRepo<SalaryAssignment>(services);
        AddRepo<BracketTable>(services);
        AddRepo<BracketRow>(services);
        AddRepo<PayRun>(services);
        AddRepo<Payslip>(services);
        AddRepo<PayslipLine>(services);
        AddRepo<Vendor>(services);
        AddRepo<Account>(services);
        AddRepo<JournalEntry>(services);
        AddRepo<JournalLine>(services);
        AddRepo<FiscalYear>(services);
        AddRepo<ExchangeRate>(services);
        AddRepo<DocumentSequence>(services);
        AddRepo<AccountPeriodBalance>(services);

        // UnitOfWork（让 ExecuteInUnitOfWorkAsync 走真实延迟保存路径）
        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(PayrollTestDbContext) });
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        // EventBus（无处理器，验证发布路径不炸）
        services.AddSingleton<IEventBus>(sp =>
            new LocalEventBus(sp, sp.GetRequiredService<ILogger<LocalEventBus>>()));

        // Finance 总账栈（过账/付款/作废经 ILedgerPostingService 扩展面）。
        // ⚠️ 这是 FinanceModule 注册图的手工镜像：Finance 服务新增构造依赖时必须在此同步
        // 补注册,否则本套件运行期 DI 解析崩(2026-07-15 BalanceSummaryReader 传导 32 例即此)。
        // 深修(Finance 暴露测试注册扩展)受 harness 用测试仓储+Mock IEntityManager 组最小图约束,暂维持镜像。
        services.AddScoped<IDocumentNumberService, DocumentNumberService>();
        services.AddScoped<IChartOfAccountsService, ChartOfAccountsService>();
        services.AddScoped<IExchangeRateService, ExchangeRateService>();
        services.AddScoped<IFiscalYearService, FiscalYearService>();
        services.AddScoped<LedgerPostingEngine>();
        services.AddScoped<BalanceSummaryMaintainer>();
        services.AddScoped<BalanceSummaryReader>();
        services.AddScoped<PostingGuardRunner>();
        services.AddScoped<IJournalEntryService, JournalEntryService>();
        services.AddScoped<ILedgerPostingService, LedgerPostingService>();

        // Finance 侧：影子供应商衔接
        services.AddScoped<IVendorService, VendorService>();

        // Payroll 服务
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<ISalaryComponentService, SalaryComponentService>();
        services.AddScoped<ISalaryStructureService, SalaryStructureService>();
        services.AddScoped<IBracketTableService, BracketTableService>();
        services.AddScoped<ISalaryFormulaEvaluator, NCalcSalaryFormulaEvaluator>();
        services.AddScoped<PayslipCalculator>();
        services.AddScoped<PayrollPostingHelper>();
        services.AddScoped<IPayRunService, PayRunService>();
        services.AddScoped<ICountryPackService, CountryPackService>();

        ConfigureExtraServices(services);
    }

    /// <summary>
    /// 派生测试可覆盖以追加/覆盖服务（如注册测试用计算钩子、过账守卫、country pack，
    /// 或 post-configure 调小 MaxLinesPerEntry 验证行数分块）。
    /// </summary>
    protected virtual void ConfigureExtraServices(IServiceCollection services)
    {
    }

    /// <summary>播种默认科目表（断言成功，含 2400 Wages Payable / WagesPayable 角色）</summary>
    protected async Task SeedCoaAsync()
    {
        var result = await InScopeAsync<IChartOfAccountsService, Result<int>>(s => s.SeedDefaultAsync());
        result.Succeeded.ShouldBeTrue(result.Message);
    }

    /// <summary>按编码查询科目 Id</summary>
    protected async Task<Guid> AccountIdByCodeAsync(string code)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<Account, Guid>>();
        var account = await repo.FirstOrDefaultAsync(a => a.Code == code);
        account.ShouldNotBeNull($"account {code}");
        return account.Id;
    }

    /// <summary>创建带科目映射的薪资组件（断言成功，返回组件 Id）</summary>
    protected async Task<Guid> ComponentWithAccountsAsync(
        string code, SalaryComponentType type, string? formula,
        string? expenseAccountCode = null, string? liabilityAccountCode = null,
        string? condition = null, decimal? defaultAmount = null)
    {
        Guid? expenseId = expenseAccountCode == null ? null : await AccountIdByCodeAsync(expenseAccountCode);
        Guid? liabilityId = liabilityAccountCode == null ? null : await AccountIdByCodeAsync(liabilityAccountCode);

        var result = await InScopeAsync<ISalaryComponentService, Result<SalaryComponentDto>>(s => s.CreateAsync(new CreateSalaryComponentDto
        {
            Code = code,
            Name = code,
            Type = type,
            Formula = formula,
            Condition = condition,
            DefaultAmount = defaultAmount,
            ExpenseAccountId = expenseId,
            LiabilityAccountId = liabilityId
        }));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Id;
    }

    /// <summary>为员工创建薪资分配（断言成功）</summary>
    protected async Task AssignAsync(Guid employeeId, Guid structureId, decimal baseAmount, DateTime effectiveFrom)
    {
        var result = await InScopeAsync<IEmployeeService, Result<SalaryAssignmentDto>>(s => s.CreateAssignmentAsync(employeeId, new CreateSalaryAssignmentDto
        {
            StructureId = structureId,
            BaseAmount = baseAmount,
            EffectiveFrom = effectiveFrom
        }));
        result.Succeeded.ShouldBeTrue(result.Message);
    }

    /// <summary>创建发薪批次草稿（断言成功，返回 Id）</summary>
    protected async Task<Guid> CreateRunAsync(DateTime periodStart, DateTime periodEnd, DateTime payDate, Guid? structureId = null)
    {
        var result = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CreateAsync(new CreatePayRunDto
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            PayDate = payDate,
            Frequency = PayFrequency.Monthly,
            StructureId = structureId
        }));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Id;
    }

    /// <summary>
    /// 标准薪酬场景：COA + BASIC(earning=BASE, exp 5300) + TAX(deduction=GROSS*0.10, liab 2200)
    /// + PENSION(employer=GROSS*0.05, exp 5300, liab 2100) + 结构 + 员工 + 分配。
    /// base=1000 时：gross 1000 / tax 100 / pension 50 / net 900 / employerCost 50。
    /// </summary>
    protected async Task<(Guid StructureId, Guid EmployeeId)> StandardScenarioAsync(string employeeCode = "EMP1", decimal baseAmount = 1000m)
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var tax = await ComponentWithAccountsAsync("TAX", SalaryComponentType.Deduction, "GROSS * 0.10", liabilityAccountCode: "2200");
        var pension = await ComponentWithAccountsAsync("PENSION", SalaryComponentType.EmployerContribution, "GROSS * 0.05",
            expenseAccountCode: "5300", liabilityAccountCode: "2100");

        var structure = await CreateStructureAsync("Standard",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = tax, Sequence = 2 },
            new SalaryStructureLineInputDto { ComponentId = pension, Sequence = 3 });
        structure.Succeeded.ShouldBeTrue(structure.Message);

        var employee = await CreateEmployeeAsync(employeeCode, employeeCode);
        await AssignAsync(employee.Id, structure.Data!.Id, baseAmount, new DateTime(2026, 1, 1));
        return (structure.Data!.Id, employee.Id);
    }

    /// <summary>过账凭证的借贷合计（用于恒等式断言）</summary>
    protected async Task<(decimal Debit, decimal Credit)> JournalTotalsAsync(Guid journalEntryId)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<JournalLine, Guid>>();
        var lines = await repo.ToListAsync(l => l.JournalEntryId == journalEntryId);
        return (lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<PayrollTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<PayrollTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IReadOnlyRepository<TEntity, Guid>>(sp =>
            sp.GetRequiredService<IRepository<TEntity, Guid>>());
    }

    /// <summary>
    /// 在独立 scope 中执行一次服务操作（每次操作=一个新的服务实例，贴近真实请求生命周期）
    /// </summary>
    protected async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        using var scope = ServiceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TService>();
        return await action(svc);
    }

    /// <summary>
    /// 在独立 scope 中读取实体最新状态
    /// </summary>
    protected async Task<TEntity?> ReloadAsync<TEntity>(Guid id) where TEntity : class, IEntity<Guid>
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<TEntity, Guid>>();
        return await repo.FirstOrDefaultAsync(e => e.Id == id);
    }

    /// <summary>
    /// 在独立 scope 中统计实体数量
    /// </summary>
    protected async Task<int> CountAsync<TEntity>(Func<TEntity, bool> predicate) where TEntity : class, IEntity<Guid>
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<TEntity, Guid>>();
        var all = await repo.ToListAsync(_ => true);
        return all.Count(predicate);
    }

    /// <summary>
    /// 创建员工（断言成功）
    /// </summary>
    protected async Task<EmployeeDto> CreateEmployeeAsync(string code, string name, string? attributesJson = null)
    {
        var result = await InScopeAsync<IEmployeeService, Result<EmployeeDto>>(s => s.CreateAsync(new CreateEmployeeDto
        {
            Code = code,
            Name = name,
            AttributesJson = attributesJson
        }));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!;
    }

    /// <summary>
    /// 创建薪资组件（断言成功）
    /// </summary>
    protected async Task<SalaryComponentDto> CreateComponentAsync(
        string code, SalaryComponentType type = SalaryComponentType.Earning,
        string? formula = null, string? condition = null, decimal? defaultAmount = null)
    {
        var result = await InScopeAsync<ISalaryComponentService, Result<SalaryComponentDto>>(s => s.CreateAsync(new CreateSalaryComponentDto
        {
            Code = code,
            Name = code,
            Type = type,
            Formula = formula,
            Condition = condition,
            DefaultAmount = defaultAmount
        }));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!;
    }

    /// <summary>
    /// 创建薪资结构（返回原始 Result，便于断言失败路径）
    /// </summary>
    protected Task<Result<SalaryStructureDto>> CreateStructureAsync(string name, params SalaryStructureLineInputDto[] lines)
        => InScopeAsync<ISalaryStructureService, Result<SalaryStructureDto>>(s => s.CreateAsync(new CreateSalaryStructureDto
        {
            Name = name,
            Frequency = PayFrequency.Monthly,
            Lines = lines.ToList()
        }));

    /// <summary>
    /// 创建税级表（返回原始 Result，便于断言失败路径）
    /// </summary>
    protected Task<Result<BracketTableDto>> CreateBracketTableAsync(string code, DateTime effectiveFrom, params BracketRowInputDto[] rows)
        => InScopeAsync<IBracketTableService, Result<BracketTableDto>>(s => s.CreateAsync(new CreateBracketTableDto
        {
            Code = code,
            Name = code,
            EffectiveFrom = effectiveFrom,
            Rows = rows.ToList()
        }));

    /// <summary>
    /// 标准四档累进表行（含速算扣除数；QuickDeduction 与逐级累进在数值上一致）
    /// </summary>
    protected static BracketRowInputDto[] StandardQuickRows() =>
    [
        new() { Sequence = 1, LowerBound = 0, UpperBound = 3000, Rate = 0.03m, QuickDeduction = 0 },
        new() { Sequence = 2, LowerBound = 3000, UpperBound = 12000, Rate = 0.10m, QuickDeduction = 210 },
        new() { Sequence = 3, LowerBound = 12000, UpperBound = 25000, Rate = 0.20m, QuickDeduction = 1410 },
        new() { Sequence = 4, LowerBound = 25000, UpperBound = null, Rate = 0.25m, QuickDeduction = 2660 }
    ];

    /// <summary>
    /// 与 <see cref="StandardQuickRows"/> 同档次的纯累进表行（无速算扣除数）
    /// </summary>
    protected static BracketRowInputDto[] StandardProgressiveRows() =>
    [
        new() { Sequence = 1, LowerBound = 0, UpperBound = 3000, Rate = 0.03m },
        new() { Sequence = 2, LowerBound = 3000, UpperBound = 12000, Rate = 0.10m },
        new() { Sequence = 3, LowerBound = 12000, UpperBound = 25000, Rate = 0.20m },
        new() { Sequence = 4, LowerBound = 25000, UpperBound = null, Rate = 0.25m }
    ];
}
