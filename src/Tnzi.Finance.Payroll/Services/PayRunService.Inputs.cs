namespace Tnzi.Finance.Payroll.Services;

/// <summary>
/// 发薪批次服务 —— 一次性输入子资源（奖金 / 补发 / 罚扣 / 预支归还 / 更正）
/// </summary>
/// <remarks>
/// 设计要点见 <see cref="PayRunInput"/> 与 <c>PayrollFormulaFunctions.Input</c>。
/// 本文件只负责录入面的守卫：一笔录进去却不生效的金额，比一个 400 危险得多，
/// 所以"这个组件真的会读 <c>Input()</c> 吗"在录入那一刻就问清楚。
/// </remarks>
public partial class PayRunService
{
    public async Task<Result<List<PayRunInputDto>>> GetInputsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await _runRepo.AnyAsync(r => r.Id == id, cancellationToken))
            return Fail<List<PayRunInputDto>>("Pay run not found.", 404);

        var inputs = await _inputRepo.AsNoTracking()
            .Where(i => i.PayRunId == id)
            .ToListAsync(cancellationToken);
        if (inputs.Count == 0)
            return Ok(new List<PayRunInputDto>());

        var employeeIds = inputs.Select(i => i.EmployeeId).Distinct().ToList();
        var componentIds = inputs.Select(i => i.ComponentId).Distinct().ToList();

        var employees = await _employeeRepo.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken);
        var components = await _componentRepo.AsNoTracking()
            .Where(c => componentIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var dtos = inputs
            .Select(i => ToInputDto(i, employees.GetValueOrDefault(i.EmployeeId), components.GetValueOrDefault(i.ComponentId)))
            .OrderBy(d => d.EmployeeCode, StringComparer.Ordinal)
            .ThenBy(d => d.ComponentCode, StringComparer.Ordinal)
            .ToList();

        return Ok(dtos);
    }

    public async Task<Result<PayRunInputDto>> SetInputAsync(Guid id, SetPayRunInputDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var run = await _runRepo.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run == null)
            return Fail<PayRunInputDto>("Pay run not found.", 404);

        var gate = GateInputEditing(run);
        if (!gate.Succeeded)
            return Fail<PayRunInputDto>(gate.Message!, gate.Code ?? 409);

        var resolved = await ResolveInputTargetAsync(run, input.EmployeeId, input.ComponentId, cancellationToken);
        if (!resolved.Succeeded)
            return Fail<PayRunInputDto>(resolved.Message!, resolved.Code ?? 400);

        var (employee, component) = resolved.Data;

        // 负的收入/扣减/雇主承担项就是一次没人申报的反向发放——与 DefaultAmount /
        // AmountOverride 同一口径。备注项是具名中间量，天然带符号。
        if (PayrollAmountRules.IsNegativeMonetary(component.Type, input.Amount))
            return Fail<PayRunInputDto>("A one-time input cannot be negative for an earning, deduction or employer-contribution component.", 400);

        var note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();

        PayRunInput entity;
        try
        {
            // ★落库与重算同一事务。分开做的话，重算失败（并发 409）会留下
            // "输入已存、工资单未重算"的批次——状态是 Calculated、数字却不含刚批准那笔钱，
            // 而它照样可以过账。这正是这个功能从头到尾在防的那一种失效。
            // 注意 ExecuteInUnitOfWorkAsync 只在**异常**时回滚（返回失败 Result 仍会提交），
            // 所以重算失败必须以 PayrollUnitOfWorkAbortException 传出去。
            entity = await ExecuteInUnitOfWorkAsync(async ct =>
            {
                var saved = await UpsertInputAsync(id, input, note, ct);

                var recalc = await RecalculateForInputChangeAsync(run, input.EmployeeId, ct);
                if (!recalc.Succeeded)
                    throw new PayrollUnitOfWorkAbortException(recalc);

                return saved;
            }, cancellationToken);
        }
        catch (PayrollUnitOfWorkAbortException ex)
        {
            return Fail<PayRunInputDto>(ex.Result.Message ?? "Recalculation failed.", ex.Result.Code ?? 400);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<PayRunInputDto>("A one-time input already exists for this employee and component; reload and edit it.", 409);
        }

        return Ok(ToInputDto(entity, employee, component));
    }

    /// <summary>(批次, 员工, 组件) 已有则改额，没有则新建。唯一索引冲突交给调用方转 409。</summary>
    private async Task<PayRunInput> UpsertInputAsync(Guid runId, SetPayRunInputDto input, string? note, CancellationToken cancellationToken)
    {
        var existing = await _inputRepo.FirstOrDefaultAsync(
            i => i.PayRunId == runId && i.EmployeeId == input.EmployeeId && i.ComponentId == input.ComponentId,
            cancellationToken);

        if (existing != null)
        {
            existing.Amount = input.Amount;
            existing.Note = note;
            await _inputRepo.UpdateAsync(existing, cancellationToken);
            await _inputRepo.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var entity = new PayRunInput
        {
            PayRunId = runId,
            EmployeeId = input.EmployeeId,
            ComponentId = input.ComponentId,
            Amount = input.Amount,
            Note = note
        };
        await _inputRepo.InsertAsync(entity, cancellationToken);
        await _inputRepo.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<Result> DeleteInputAsync(Guid id, Guid inputId, CancellationToken cancellationToken = default)
    {
        var run = await _runRepo.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run == null)
            return Fail("Pay run not found.", 404);

        var gate = GateInputEditing(run);
        if (!gate.Succeeded)
            return gate;

        var entity = await _inputRepo.FirstOrDefaultAsync(i => i.Id == inputId && i.PayRunId == id, cancellationToken);
        if (entity == null)
            return Fail("One-time input not found.", 404);

        var employeeId = entity.EmployeeId;
        try
        {
            // 与 SetInputAsync 同理：删除与重算要么一起成立，要么都不成立。
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                await _inputRepo.DeleteAsync(entity, ct);
                await _inputRepo.SaveChangesAsync(ct);

                var recalc = await RecalculateForInputChangeAsync(run, employeeId, ct);
                if (!recalc.Succeeded)
                    throw new PayrollUnitOfWorkAbortException(recalc);
            }, cancellationToken);
        }
        catch (PayrollUnitOfWorkAbortException ex)
        {
            return Fail(ex.Result.Message ?? "Recalculation failed.", ex.Result.Code ?? 400);
        }

        return Ok();
    }

    /// <summary>
    /// 录入面的状态门：Draft（首次计算之前先录，真实发薪的顺序）与 Calculated（补录/更正）。
    /// 过账之后再改就与总账对不上了；外部摄取的批次不由本模块计算，改输入没有任何作用。
    /// </summary>
    private Result GateInputEditing(PayRun run)
    {
        if (run.Source != PayRunSource.Internal)
            return Fail("Only internal pay runs read one-time inputs; external and opening-balance runs are ingested as-is.", 409);
        if (run.Status is not (PayRunStatus.Draft or PayRunStatus.Calculated))
            return Fail("One-time inputs can only be changed while the pay run is a draft or in the Calculated state.", 409);
        return Ok();
    }

    /// <summary>
    /// 解析并校验录入目标：员工在本批次的圈选范围内，组件在该员工本期生效的结构里，
    /// 且那一行的生效公式**真的会读** <c>Input()</c>。
    /// </summary>
    /// <remarks>
    /// 三道守卫针对的是同一种失效：录进去了，跑批时谁也不读它。这类"存下来了但没接上"
    /// 不会报错、不会红、只会让实发额少一笔，要到员工问起来才发现。
    /// </remarks>
    private async Task<Result<(Employee Employee, SalaryComponent Component)>> ResolveInputTargetAsync(
        PayRun run, Guid employeeId, Guid componentId, CancellationToken cancellationToken)
    {
        var employee = await _employeeRepo.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (employee == null)
            return Result.Failure<(Employee, SalaryComponent)>("Employee not found.", 404);

        var component = await _componentRepo.AsNoTracking().FirstOrDefaultAsync(c => c.Id == componentId, cancellationToken);
        if (component == null)
            return Result.Failure<(Employee, SalaryComponent)>("Salary component not found.", 404);
        if (!component.IsActive)
            return Result.Failure<(Employee, SalaryComponent)>($"Salary component '{component.Code}' is inactive.", 400);

        var periodEnd = run.PeriodEnd.ToUtcDate();

        // 圈选判据与计算器同源（PayRunEligibility）——数据库侧的 Where 只是少拉数据的预筛。
        var candidates = await _assignmentRepo.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.EffectiveFrom <= periodEnd)
            .ToListAsync(cancellationToken);
        var assignment = PayRunEligibility.PickEffective(candidates, periodEnd);

        var eligibility = PayRunEligibility.Evaluate(run, employee, assignment);
        if (eligibility != PayRunEligibilityStatus.Eligible)
            return Result.Failure<(Employee, SalaryComponent)>(DescribeIneligibility(employee, eligibility), 400);

        // Eligible 蕴含分配非空（Evaluate 对 null 返回 NoAssignment），编译器推不出来。
        var effective = Check.NotNull(assignment);

        var structure = await _structureRepo.AsNoTracking()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == effective.StructureId, cancellationToken);
        var structureLine = structure?.Lines.FirstOrDefault(l => l.ComponentId == componentId);

        // 可读性判据与计算端同源（PayRunInputBinding）——跑批时那一侧会再核一次。
        var binding = PayRunInputBinding.Evaluate(_evaluator, structureLine, component);
        if (binding != PayRunInputBindingStatus.Readable)
            return Result.Failure<(Employee, SalaryComponent)>(DescribeUnreadableInput(employee, component, binding), 400);

        return Result.Success((employee, component));
    }

    /// <summary>把"读不到"的原因翻成录入端的 400（判据本身在 <see cref="PayRunInputBinding"/>）</summary>
    private static string DescribeUnreadableInput(Employee employee, SalaryComponent component, PayRunInputBindingStatus status) => status switch
    {
        PayRunInputBindingStatus.NotOnStructure =>
            $"Component '{component.Code}' is not part of the salary structure assigned to employee '{employee.Code}' for this period, " +
            "so a one-time amount entered against it would never be read. Add the component to the structure first.",
        PayRunInputBindingStatus.PinnedAmount =>
            $"The structure line for component '{component.Code}' pins a fixed amount, which takes precedence over its formula, " +
            "so a one-time input would never be read. Clear the line's AmountOverride first.",
        _ =>
            $"The formula for component '{component.Code}' never calls {PayrollFormulaFunctions.Input}(), " +
            "so a one-time amount entered against it would be stored and then silently ignored. " +
            $"Give the component a formula that reads it, for example \"{PayrollFormulaFunctions.Input}()\" " +
            $"or \"BASE + {PayrollFormulaFunctions.Input}()\"."
    };

    /// <summary>把不合格原因翻成录入端说得清的消息（判据本身在 <see cref="PayRunEligibility"/>）</summary>
    private static string DescribeIneligibility(Employee employee, PayRunEligibilityStatus status) => status switch
    {
        PayRunEligibilityStatus.NoAssignment =>
            $"Employee '{employee.Code}' has no salary assignment effective in this period.",
        PayRunEligibilityStatus.StructureMismatch =>
            $"Employee '{employee.Code}' is not included in this pay run (the run filters on a different salary structure).",
        _ => $"Employee '{employee.Code}' is not included in this pay run."
    };

    /// <summary>
    /// 输入变更后，把该员工那一张工资单重算回与输入一致的状态（仅 Calculated 态有工资单可重算）。
    /// </summary>
    /// <remarks>
    /// 出勤天数取工资单现值而不是周期天数——否则"录一笔奖金"会顺手把已经改过的出勤天数冲回去。
    /// </remarks>
    private async Task<Result> RecalculateForInputChangeAsync(PayRun run, Guid employeeId, CancellationToken cancellationToken)
    {
        if (run.Status != PayRunStatus.Calculated)
            return Ok();

        var payslip = await _payslipRepo.AsQueryable(true).Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.PayRunId == run.Id && p.EmployeeId == employeeId, cancellationToken);
        if (payslip == null)
            return Ok();

        return await RecalculatePayslipAsync(run, payslip, payslip.WorkedDays, cancellationToken);
    }

    private static PayRunInputDto ToInputDto(PayRunInput entity, Employee? employee, SalaryComponent? component) => new()
    {
        Id = entity.Id,
        PayRunId = entity.PayRunId,
        EmployeeId = entity.EmployeeId,
        EmployeeCode = employee?.Code ?? string.Empty,
        EmployeeName = employee?.Name ?? string.Empty,
        ComponentId = entity.ComponentId,
        ComponentCode = component?.Code ?? string.Empty,
        ComponentName = component?.Name ?? string.Empty,
        ComponentType = component?.Type ?? default,
        Amount = entity.Amount,
        Note = entity.Note,
        CreationTime = entity.CreationTime
    };
}
