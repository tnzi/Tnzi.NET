using System.Text.RegularExpressions;

namespace Tnzi.Finance.Payroll.Services;

/// <summary>
/// 薪资组件服务
/// </summary>
/// <remarks>
/// 保存期校验 = 编码规范 + 公式/条件语法与函数白名单（经
/// <see cref="ISalaryFormulaEvaluator.GetVariables"/>）+ 自引用拒绝。
/// 跨组件依赖序校验属于结构语义，在结构保存时进行；更新组件时对引用它的结构按同一份规则
/// （<see cref="SalaryStructureRules"/>）回查一次，见 <see cref="ValidateReferencingStructuresAsync"/>。
/// </remarks>
public partial class SalaryComponentService : ApplicationService, ISalaryComponentService
{
    private readonly IRepository<SalaryComponent, Guid> _componentRepository;
    private readonly IRepository<SalaryStructureLine, Guid> _lineRepository;
    private readonly IReadOnlyRepository<SalaryStructure, Guid> _structureRepository;
    private readonly IReadOnlyRepository<PayslipLine, Guid> _payslipLineRepository;
    private readonly ISalaryFormulaEvaluator _evaluator;
    private readonly IEnumerable<IPayslipCalculationHook> _hooks;

    public SalaryComponentService(
        IServiceProvider serviceProvider,
        IRepository<SalaryComponent, Guid> componentRepository,
        IRepository<SalaryStructureLine, Guid> lineRepository,
        IReadOnlyRepository<SalaryStructure, Guid> structureRepository,
        IReadOnlyRepository<PayslipLine, Guid> payslipLineRepository,
        ISalaryFormulaEvaluator evaluator,
        IEnumerable<IPayslipCalculationHook> hooks) : base(serviceProvider)
    {
        _componentRepository = Check.NotNull(componentRepository);
        _lineRepository = Check.NotNull(lineRepository);
        _structureRepository = Check.NotNull(structureRepository);
        _payslipLineRepository = Check.NotNull(payslipLineRepository);
        _evaluator = Check.NotNull(evaluator);
        _hooks = Check.NotNull(hooks);
    }

    public async Task<Result<IPagedList<SalaryComponentDto>>> GetPagedAsync(SalaryComponentQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _componentRepository.AsNoTracking()
            .Filter(query)
            .OrderBy(c => c.Code)
            .ProjectTo<SalaryComponent, SalaryComponentDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<SalaryComponentDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var component = await _componentRepository.GetAsync(id, cancellationToken);
        if (component == null)
            return Fail<SalaryComponentDto>("Salary component not found.", 404);

        return Ok(component.MapTo<SalaryComponentDto>());
    }

    public async Task<Result<SalaryComponentDto>> CreateAsync(CreateSalaryComponentDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var validation = await ValidateAsync(input, excludeId: null, cancellationToken);
        if (!validation.Succeeded)
            return Fail<SalaryComponentDto>(validation.Message ?? "Invalid salary component.", validation.Code ?? 400);

        var component = new SalaryComponent();
        Apply(component, input, isActive: true);

        try
        {
            await _componentRepository.InsertAsync(component, cancellationToken);
            await _componentRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<SalaryComponentDto>($"Salary component code '{component.Code}' already exists.", 409);
        }

        return Ok(component.MapTo<SalaryComponentDto>());
    }

    public async Task<Result<SalaryComponentDto>> UpdateAsync(Guid id, UpdateSalaryComponentDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var component = await _componentRepository.GetAsync(id, cancellationToken);
        if (component == null)
            return Fail<SalaryComponentDto>("Salary component not found.", 404);

        var validation = await ValidateAsync(input, excludeId: id, cancellationToken);
        if (!validation.Succeeded)
            return Fail<SalaryComponentDto>(validation.Message ?? "Invalid salary component.", validation.Code ?? 400);

        // ★ 编码是 Ytd() 的聚合键：工资单行快照的是 ComponentCode 字符串，YTD 按它分组、Ytd('CODE') 按它查，
        // 查不到就取 0（这个员工今年还没有过这一项，是正常的）。年中改名之后 Ytd('新码') 只含改名后的批次，
        // 改名前已扣的供款全部消失于基数 —— 法定上限永不封顶，没有任何报错。
        // 判据与「被结构行引用禁删」相同：有历史就拒绝，另建一个组件。
        var normalizedCode = input.Code.Trim().ToUpperInvariant();
        if (!string.Equals(normalizedCode, component.Code, StringComparison.Ordinal)
            && await _payslipLineRepository.AnyAsync(l => l.ComponentId == id, cancellationToken))
        {
            return Fail<SalaryComponentDto>(
                "The component code cannot be changed once it appears on a payslip; year-to-date accumulation is keyed by code. Create a new component instead.",
                409);
        }

        var structureCheck = await ValidateReferencingStructuresAsync(component, input, cancellationToken);
        if (!structureCheck.Succeeded)
            return Fail<SalaryComponentDto>(structureCheck.Message ?? "Invalid salary component.", structureCheck.Code ?? 400);

        Apply(component, input, input.IsActive);

        try
        {
            await _componentRepository.UpdateAsync(component, cancellationToken);
            await _componentRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<SalaryComponentDto>($"Salary component code '{component.Code}' already exists.", 409);
        }

        return Ok(component.MapTo<SalaryComponentDto>());
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var component = await _componentRepository.GetAsync(id, cancellationToken);
        if (component == null)
            return Fail("Salary component not found.", 404);

        if (await _lineRepository.AnyAsync(l => l.ComponentId == id, cancellationToken))
            return Fail("The component is referenced by one or more salary structures and cannot be deleted.", 409);

        await _componentRepository.DeleteAsync(component, cancellationToken);
        return Ok();
    }

    public async Task<SalaryComponent?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(code);
        var normalized = code.Trim().ToUpperInvariant();
        return await _componentRepository.FindAsync(c => c.Code == normalized, cancellationToken);
    }

    private async Task<Result> ValidateAsync(CreateSalaryComponentDto input, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Code))
            return Fail("Component code is required.");
        if (string.IsNullOrWhiteSpace(input.Name))
            return Fail("Component name is required.");
        var tooLong = PayrollFieldLimits.FirstTooLong(
            (input.Name.Trim(), PayrollFieldLimits.NameMaxLength, "component name"),
            (input.Description, PayrollFieldLimits.DescriptionMaxLength, "description"));
        if (tooLong != null)
            return Fail(tooLong, 400);
        if (!Enum.IsDefined(input.Type))
            return Fail("Component type must be Earning, Deduction, EmployerContribution or Informational.");
        // 符号规则只有 PayrollAmountRules 一份（计算器 / 一次性输入 / 外部摄取 / 钉死额同一判据）。
        if (input.DefaultAmount is { } defaultAmount && PayrollAmountRules.IsNegativeMonetary(input.Type, defaultAmount))
            return Fail("DefaultAmount cannot be negative for an Earning, Deduction or EmployerContribution component.");
        // 备注项不产生分录，所以挂在它上面的科目**永远不会被用到**。静默忽略一个
        // 明确填写的配置，比拒绝它更糟：填的人会以为自己已经把账接好了。
        if (input.Type == SalaryComponentType.Informational
            && (input.ExpenseAccountId.HasValue || input.LiabilityAccountId.HasValue))
        {
            return Fail("An Informational component posts nothing, so it must not carry an expense or liability account. Clear the account, or change the type if the amount really belongs in the ledger.");
        }

        var code = input.Code.Trim().ToUpperInvariant();
        if (code.Length > 64 || !ComponentCodeRegex().IsMatch(code))
            return Fail("Component code must start with a letter and contain only A-Z, 0-9 and underscores (max 64 characters).");

        // 内置变量名保留——组件 Code 撞名会让公式引用产生歧义
        if (PayrollFormulaVariables.All.Contains(code))
            return Fail($"Component code '{code}' is a reserved formula variable name.");

        // 钩子声明提供的外部变量同理保留。★不挡住的话撞名是**静默错账**而不是报错：
        // 钩子在按序求值前注入标量，而组件算完会把同名键覆盖掉（PayslipCalculator 逐行写
        // context.Variables[component.Code]）——于是引用这个名字的公式读到的是费率还是某行金额，
        // 取决于它排在第几行。
        if (PayrollExternalVariables.Collect(_hooks).Contains(code))
        {
            return Fail($"Component code '{code}' is already provided as a run-time variable by a registered " +
                "calculation hook. Pick a different code, or drop the name from IPayslipCalculationHook.ProvidedVariables.");
        }

        var expressionCheck = ValidateExpressions(code, input.Formula, input.Condition);
        if (!expressionCheck.Succeeded)
            return expressionCheck;

        if (await _componentRepository.AnyAsync(c => c.Code == code && c.Id != excludeId, cancellationToken))
            return Fail($"Salary component code '{code}' already exists.", 409);

        return Ok();
    }

    /// <summary>
    /// 本次修改会不会让引用这个组件的薪资结构失效（按结构保存时的同一份规则回查）。
    /// </summary>
    /// <remarks>
    /// ★ 结构行的生效公式由「行覆盖 ∪ 组件定义」合成，所以结构合不合规不只取决于结构本身。
    /// 此前规则只在结构保存时跑：把序号靠前的税组件公式改成读 <c>GROSS</c>，或把序号靠后的扣减项
    /// 改成收入，两次保存都成功，此后每一期都对不完整的毛额计税，不落任何计算错误，照常过账并进入 YTD。
    /// 改编码同理会让后续行引用一个不存在的名字，改类型会让钉死的负额变成负的扣减项。
    /// <para>
    /// 只拒绝**本次修改引入**的失效：结构本就不合规时不挡（否则连修复它的那次组件修改也改不动），
    /// 这种结构由计算端兜底落 <c>CalculationError</c>、过不了账。
    /// </para>
    /// </remarks>
    private async Task<Result> ValidateReferencingStructuresAsync(SalaryComponent current, UpdateSalaryComponentDto input, CancellationToken cancellationToken)
    {
        var candidate = new SalaryComponent();
        Apply(candidate, input, input.IsActive);

        // 结构规则只读组件的这四项；都没变就没有可回查的。
        if (candidate.Code == current.Code && candidate.Type == current.Type
            && candidate.Formula == current.Formula && candidate.Condition == current.Condition)
        {
            return Ok();
        }

        var structureIds = await _lineRepository.AsNoTracking()
            .Where(l => l.ComponentId == current.Id)
            .Select(l => l.StructureId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (structureIds.Count == 0)
            return Ok();

        var structures = await _structureRepository.AsQueryable()
            .Include(s => s.Lines)
            .Where(s => structureIds.Contains(s.Id))
            .ToListAsync(cancellationToken);

        var componentIds = structures.SelectMany(s => s.Lines).Select(l => l.ComponentId).Distinct().ToList();
        var before = await _componentRepository.AsNoTracking()
            .Where(c => componentIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        var after = new Dictionary<Guid, SalaryComponent>(before) { [current.Id] = candidate };
        var externalVariables = PayrollExternalVariables.Collect(_hooks);

        foreach (var structure in structures.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            var lines = structure.Lines.Select(SalaryStructureRules.ToSpec).ToList();
            // 停用另有其义（不再加进新结构），不在回查之列。
            var afterCheck = SalaryStructureRules.ValidateLines(lines, after, externalVariables, _evaluator, requireActiveComponents: false);
            if (afterCheck.Succeeded)
                continue;

            var beforeCheck = SalaryStructureRules.ValidateLines(lines, before, externalVariables, _evaluator, requireActiveComponents: false);
            if (!beforeCheck.Succeeded)
                continue;

            return Fail($"This change would make salary structure '{structure.Name}' invalid: {afterCheck.Message}", 400);
        }

        return Ok();
    }

    /// <summary>
    /// 语法 + 函数白名单（经 GetVariables）+ 自引用拒绝
    /// </summary>
    private Result ValidateExpressions(string code, string? formula, string? condition)
    {
        if (!string.IsNullOrWhiteSpace(formula))
        {
            var variables = _evaluator.GetVariables(formula);
            if (!variables.Succeeded)
                return Fail($"Invalid formula: {variables.Message}", 400);
            if (variables.Data!.Contains(code, StringComparer.Ordinal))
                return Fail("The formula must not reference the component's own code.", 400);
        }

        if (!string.IsNullOrWhiteSpace(condition))
        {
            var variables = _evaluator.GetVariables(condition);
            if (!variables.Succeeded)
                return Fail($"Invalid condition: {variables.Message}", 400);
            // 条件先于组件求值，自身值彼时尚不存在
            if (variables.Data!.Contains(code, StringComparer.Ordinal))
                return Fail("The condition must not reference the component's own code.", 400);
        }

        return Ok();
    }

    private static void Apply(SalaryComponent component, CreateSalaryComponentDto input, bool isActive)
    {
        component.Code = input.Code.Trim().ToUpperInvariant();
        component.Name = input.Name.Trim();
        component.Type = input.Type;
        component.Formula = string.IsNullOrWhiteSpace(input.Formula) ? null : input.Formula.Trim();
        component.Condition = string.IsNullOrWhiteSpace(input.Condition) ? null : input.Condition.Trim();
        component.DefaultAmount = input.DefaultAmount;
        component.ExpenseAccountId = input.ExpenseAccountId;
        component.LiabilityAccountId = input.LiabilityAccountId;
        component.Description = input.Description;
        component.IsActive = isActive;
    }

    [GeneratedRegex("^[A-Z][A-Z0-9_]*$")]
    private static partial Regex ComponentCodeRegex();
}
