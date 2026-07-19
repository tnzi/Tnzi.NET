namespace Tnzi.Finance.Payroll.Services;

/// <summary>
/// 薪资结构服务
/// </summary>
/// <remarks>
/// 保存期静态校验（缓冲式，任何失败先于写入返回）：
/// 组件存在且启用、序号/组件不重复、逐行提取变量
/// （<see cref="ISalaryFormulaEvaluator.GetVariables"/>），
/// 允许集 = 内置变量 ∪ 更早序号行的组件 Code——越序/未知变量 400。
/// </remarks>
public class SalaryStructureService : ApplicationService, ISalaryStructureService
{
    private readonly IRepository<SalaryStructure, Guid> _structureRepository;
    private readonly IRepository<SalaryStructureLine, Guid> _lineRepository;
    private readonly IRepository<SalaryComponent, Guid> _componentRepository;
    private readonly IRepository<SalaryAssignment, Guid> _assignmentRepository;
    private readonly ISalaryFormulaEvaluator _evaluator;

    public SalaryStructureService(
        IServiceProvider serviceProvider,
        IRepository<SalaryStructure, Guid> structureRepository,
        IRepository<SalaryStructureLine, Guid> lineRepository,
        IRepository<SalaryComponent, Guid> componentRepository,
        IRepository<SalaryAssignment, Guid> assignmentRepository,
        ISalaryFormulaEvaluator evaluator) : base(serviceProvider)
    {
        _structureRepository = Check.NotNull(structureRepository);
        _lineRepository = Check.NotNull(lineRepository);
        _componentRepository = Check.NotNull(componentRepository);
        _assignmentRepository = Check.NotNull(assignmentRepository);
        _evaluator = Check.NotNull(evaluator);
    }

    public async Task<Result<IPagedList<SalaryStructureListDto>>> GetPagedAsync(SalaryStructureQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _structureRepository.AsNoTracking()
            .Filter(query)
            .OrderBy(s => s.Name)
            .ProjectTo<SalaryStructure, SalaryStructureListDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<SalaryStructureDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var structure = await _structureRepository.AsNoTracking()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (structure == null)
            return Fail<SalaryStructureDto>("Salary structure not found.", 404);

        return Ok(await ToDtoAsync(structure, cancellationToken));
    }

    public async Task<Result<SalaryStructureDto>> CreateAsync(CreateSalaryStructureDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var validation = await ValidateAsync(input, cancellationToken);
        if (!validation.Succeeded)
            return Fail<SalaryStructureDto>(validation.Message ?? "Invalid salary structure.", validation.Code ?? 400);

        var structure = new SalaryStructure
        {
            Name = input.Name.Trim(),
            Description = input.Description,
            Frequency = input.Frequency
        };
        AppendLines(structure, input.Lines);

        await _structureRepository.InsertAsync(structure, cancellationToken);
        await _structureRepository.SaveChangesAsync(cancellationToken);

        return Ok(await ToDtoAsync(structure, cancellationToken));
    }

    public async Task<Result<SalaryStructureDto>> UpdateAsync(Guid id, UpdateSalaryStructureDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var structure = await _structureRepository.AsQueryable(true)
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (structure == null)
            return Fail<SalaryStructureDto>("Salary structure not found.", 404);

        var validation = await ValidateAsync(input, cancellationToken);
        if (!validation.Succeeded)
            return Fail<SalaryStructureDto>(validation.Message ?? "Invalid salary structure.", validation.Code ?? 400);

        structure.Name = input.Name.Trim();
        structure.Description = input.Description;
        structure.Frequency = input.Frequency;
        structure.IsActive = input.IsActive;

        // 行硬删 + 重建 + 头更新须原子（无环境事务时仓储逐调用立即提交）
        await ExecuteInUnitOfWorkAsync<Result>(async ct =>
        {
            if (structure.Lines.Count > 0)
                await _lineRepository.DeleteManyAsync(structure.Lines.ToList(), ct);
            structure.Lines.Clear();
            AppendLines(structure, input.Lines);

            await _structureRepository.UpdateAsync(structure, ct);
            await _structureRepository.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);

        return Ok(await ToDtoAsync(structure, cancellationToken));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var structure = await _structureRepository.AsQueryable(true)
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (structure == null)
            return Fail("Salary structure not found.", 404);

        if (await _assignmentRepository.AnyAsync(a => a.StructureId == id, cancellationToken))
            return Fail("The structure is referenced by one or more salary assignments and cannot be deleted.", 409);

        // 行无软删除，随头一并物理删除；两次写入须原子
        await ExecuteInUnitOfWorkAsync<Result>(async ct =>
        {
            if (structure.Lines.Count > 0)
                await _lineRepository.DeleteManyAsync(structure.Lines.ToList(), ct);
            await _structureRepository.DeleteAsync(structure, ct);
            return Result.Success();
        }, cancellationToken);

        return Ok();
    }

    private async Task<Result> ValidateAsync(CreateSalaryStructureDto input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Fail("Structure name is required.");
        if (!Enum.IsDefined(input.Frequency))
            return Fail("Frequency must be Monthly, SemiMonthly, BiWeekly or Weekly.");
        if (input.Lines == null || input.Lines.Count == 0)
            return Fail("A salary structure requires at least one line.");

        if (input.Lines.Select(l => l.Sequence).Distinct().Count() != input.Lines.Count)
            return Fail("Line sequences must be unique.");
        if (input.Lines.Select(l => l.ComponentId).Distinct().Count() != input.Lines.Count)
            return Fail("A component can appear at most once per structure.");
        if (input.Lines.Any(l => l.AmountOverride is < 0))
            return Fail("AmountOverride cannot be negative.");

        var componentIds = input.Lines.Select(l => l.ComponentId).ToList();
        var components = await _componentRepository.AsNoTracking()
            .Where(c => componentIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        // GROSS 是按序累加的运行毛额（每 Earning 行算完才累加），引用 GROSS 的行若其后仍有 Earning 行，
        // 读到的是不完整 GROSS（例:税行排在加班费行之前会对不含加班的毛额计税，静默少税）。
        // 校验:任何引用 GROSS 的行,其序号必须 ≥ 最后一个 Earning 行的序号。
        var orderedLines = input.Lines.OrderBy(l => l.Sequence).ToList();
        var lastEarningSequence = orderedLines
            .Where(l => components.TryGetValue(l.ComponentId, out var c) && c.Type == SalaryComponentType.Earning)
            .Select(l => (int?)l.Sequence)
            .LastOrDefault();

        // 允许集从内置变量起步，随序号推进滚入已算组件的 Code
        var allowed = new HashSet<string>(PayrollFormulaVariables.All, StringComparer.Ordinal);

        foreach (var line in orderedLines)
        {
            if (!components.TryGetValue(line.ComponentId, out var component))
                return Fail($"Salary component '{line.ComponentId}' not found.", 404);
            if (!component.IsActive)
                return Fail($"Salary component '{component.Code}' is inactive and cannot be added to a structure.");

            var formula = string.IsNullOrWhiteSpace(line.FormulaOverride) ? component.Formula : line.FormulaOverride;
            var condition = string.IsNullOrWhiteSpace(line.ConditionOverride) ? component.Condition : line.ConditionOverride;

            var formulaCheck = ValidateExpression(formula, allowed, component.Code, "formula");
            if (!formulaCheck.Succeeded)
                return formulaCheck;

            var conditionCheck = ValidateExpression(condition, allowed, component.Code, "condition");
            if (!conditionCheck.Succeeded)
                return conditionCheck;

            if (lastEarningSequence.HasValue && line.Sequence < lastEarningSequence.Value
                && (ReferencesGross(formula) || ReferencesGross(condition)))
            {
                return Fail($"Line {line.Sequence} (component '{component.Code}') references {PayrollFormulaVariables.Gross}, " +
                    "but earning lines follow it at a higher sequence, so GROSS would be incomplete. " +
                    "Place all earning lines before any line that reads GROSS.", 400);
            }

            allowed.Add(component.Code);
        }

        return Ok();
    }

    private bool ReferencesGross(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var variables = _evaluator.GetVariables(expression);
        return variables.Succeeded && variables.Data!.Contains(PayrollFormulaVariables.Gross);
    }

    private Result ValidateExpression(string? expression, IReadOnlySet<string> allowed, string componentCode, string kind)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return Ok();

        var variables = _evaluator.GetVariables(expression);
        if (!variables.Succeeded)
            return Fail($"Invalid {kind} for component '{componentCode}': {variables.Message}", 400);

        foreach (var variable in variables.Data!)
        {
            if (!allowed.Contains(variable))
            {
                return Fail(
                    $"The {kind} for component '{componentCode}' references '{variable}', which is not a built-in variable " +
                    "or the code of an earlier line. Components can only reference components with a smaller sequence.", 400);
            }
        }

        return Ok();
    }

    /// <summary>
    /// 追加结构行（经导航属性挂载）。创建时头 ID 尚未生成（SaveChanges 才分配），
    /// 此处赋值等价于默认值，实际 FK 由 EF 沿导航属性在提交时回填；更新时头 ID 已存在，即显式赋值
    /// </summary>
    private static void AppendLines(SalaryStructure structure, List<SalaryStructureLineInputDto> lines)
    {
        foreach (var line in lines.OrderBy(l => l.Sequence))
        {
            structure.Lines.Add(new SalaryStructureLine
            {
                StructureId = structure.Id,
                ComponentId = line.ComponentId,
                Sequence = line.Sequence,
                FormulaOverride = string.IsNullOrWhiteSpace(line.FormulaOverride) ? null : line.FormulaOverride.Trim(),
                AmountOverride = line.AmountOverride,
                ConditionOverride = string.IsNullOrWhiteSpace(line.ConditionOverride) ? null : line.ConditionOverride.Trim()
            });
        }
    }

    private async Task<SalaryStructureDto> ToDtoAsync(SalaryStructure structure, CancellationToken cancellationToken)
    {
        var dto = new SalaryStructureDto
        {
            Id = structure.Id,
            Name = structure.Name,
            Description = structure.Description,
            Frequency = structure.Frequency,
            IsActive = structure.IsActive,
            CreationTime = structure.CreationTime
        };

        if (structure.Lines.Count == 0)
            return dto;

        var componentIds = structure.Lines.Select(l => l.ComponentId).Distinct().ToList();
        var components = await _componentRepository.AsNoTracking()
            .Where(c => componentIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        dto.Lines = structure.Lines
            .OrderBy(l => l.Sequence)
            .Select(l =>
            {
                var component = components.GetValueOrDefault(l.ComponentId);
                return new SalaryStructureLineDto
                {
                    Id = l.Id,
                    ComponentId = l.ComponentId,
                    ComponentCode = component?.Code ?? string.Empty,
                    ComponentName = component?.Name ?? string.Empty,
                    ComponentType = component?.Type ?? default,
                    Sequence = l.Sequence,
                    FormulaOverride = l.FormulaOverride,
                    AmountOverride = l.AmountOverride,
                    ConditionOverride = l.ConditionOverride
                };
            })
            .ToList();

        return dto;
    }
}
