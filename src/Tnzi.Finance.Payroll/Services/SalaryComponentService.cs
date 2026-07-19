using System.Text.RegularExpressions;

namespace Tnzi.Finance.Payroll.Services;

/// <summary>
/// 薪资组件服务
/// </summary>
/// <remarks>
/// 保存期校验 = 编码规范 + 公式/条件语法与函数白名单（经
/// <see cref="ISalaryFormulaEvaluator.GetVariables"/>）+ 自引用拒绝。
/// 跨组件依赖序校验属于结构语义，在结构保存时进行。
/// </remarks>
public partial class SalaryComponentService : ApplicationService, ISalaryComponentService
{
    private readonly IRepository<SalaryComponent, Guid> _componentRepository;
    private readonly IRepository<SalaryStructureLine, Guid> _lineRepository;
    private readonly ISalaryFormulaEvaluator _evaluator;

    public SalaryComponentService(
        IServiceProvider serviceProvider,
        IRepository<SalaryComponent, Guid> componentRepository,
        IRepository<SalaryStructureLine, Guid> lineRepository,
        ISalaryFormulaEvaluator evaluator) : base(serviceProvider)
    {
        _componentRepository = Check.NotNull(componentRepository);
        _lineRepository = Check.NotNull(lineRepository);
        _evaluator = Check.NotNull(evaluator);
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
        if (!Enum.IsDefined(input.Type))
            return Fail("Component type must be Earning, Deduction or EmployerContribution.");
        if (input.DefaultAmount is < 0)
            return Fail("DefaultAmount cannot be negative.");

        var code = input.Code.Trim().ToUpperInvariant();
        if (code.Length > 64 || !ComponentCodeRegex().IsMatch(code))
            return Fail("Component code must start with a letter and contain only A-Z, 0-9 and underscores (max 64 characters).");

        // 内置变量名保留——组件 Code 撞名会让公式引用产生歧义
        if (PayrollFormulaVariables.All.Contains(code))
            return Fail($"Component code '{code}' is a reserved formula variable name.");

        var expressionCheck = ValidateExpressions(code, input.Formula, input.Condition);
        if (!expressionCheck.Succeeded)
            return expressionCheck;

        if (await _componentRepository.AnyAsync(c => c.Code == code && c.Id != excludeId, cancellationToken))
            return Fail($"Salary component code '{code}' already exists.", 409);

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
