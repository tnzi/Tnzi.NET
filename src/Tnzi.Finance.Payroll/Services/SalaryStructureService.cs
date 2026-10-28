namespace Tnzi.Finance.Payroll.Services;

/// <summary>
/// 薪资结构服务
/// </summary>
/// <remarks>
/// 保存期静态校验（缓冲式，任何失败先于写入返回）：
/// 组件存在且启用、序号/组件不重复、逐行提取变量
/// （<see cref="ISalaryFormulaEvaluator.GetVariables"/>），
/// 允许集 = 内置变量 ∪ 更早序号行的组件 Code ∪ 计算钩子声明提供的外部变量
/// （<see cref="IPayslipCalculationHook.ProvidedVariables"/>）——越序/未知变量 400。
/// </remarks>
public class SalaryStructureService : ApplicationService, ISalaryStructureService
{
    private readonly IRepository<SalaryStructure, Guid> _structureRepository;
    private readonly IRepository<SalaryStructureLine, Guid> _lineRepository;
    private readonly IRepository<SalaryComponent, Guid> _componentRepository;
    private readonly IRepository<SalaryAssignment, Guid> _assignmentRepository;
    private readonly ISalaryFormulaEvaluator _evaluator;
    private readonly IEnumerable<IPayslipCalculationHook> _hooks;

    public SalaryStructureService(
        IServiceProvider serviceProvider,
        IRepository<SalaryStructure, Guid> structureRepository,
        IRepository<SalaryStructureLine, Guid> lineRepository,
        IRepository<SalaryComponent, Guid> componentRepository,
        IRepository<SalaryAssignment, Guid> assignmentRepository,
        ISalaryFormulaEvaluator evaluator,
        IEnumerable<IPayslipCalculationHook> hooks) : base(serviceProvider)
    {
        _structureRepository = Check.NotNull(structureRepository);
        _lineRepository = Check.NotNull(lineRepository);
        _componentRepository = Check.NotNull(componentRepository);
        _assignmentRepository = Check.NotNull(assignmentRepository);
        _evaluator = Check.NotNull(evaluator);
        _hooks = Check.NotNull(hooks);
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
        var tooLong = PayrollFieldLimits.FirstTooLong(
            (input.Name.Trim(), PayrollFieldLimits.NameMaxLength, "structure name"),
            (input.Description, PayrollFieldLimits.DescriptionMaxLength, "description"));
        if (tooLong != null)
            return Fail(tooLong, 400);
        if (!Enum.IsDefined(input.Frequency))
            return Fail("Frequency must be Monthly, SemiMonthly, BiWeekly or Weekly.");
        if (input.Lines == null || input.Lines.Count == 0)
            return Fail("A salary structure requires at least one line.");

        if (input.Lines.Select(l => l.Sequence).Distinct().Count() != input.Lines.Count)
            return Fail("Line sequences must be unique.");
        if (input.Lines.Select(l => l.ComponentId).Distinct().Count() != input.Lines.Count)
            return Fail("A component can appear at most once per structure.");

        var componentIds = input.Lines.Select(l => l.ComponentId).ToList();
        var components = await _componentRepository.AsNoTracking()
            .Where(c => componentIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        // 逐行规则（钉死额符号、只引用更早序号的行、读 GROSS 的行排在最后一个收入行之后）
        // 与组件更新回查、计算端兜底共用一份判据（SalaryStructureRules）。
        return SalaryStructureRules.ValidateLines(
            input.Lines.Select(SalaryStructureRules.ToSpec).ToList(),
            components,
            PayrollExternalVariables.Collect(_hooks),
            _evaluator,
            requireActiveComponents: true);
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
