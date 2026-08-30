namespace Tnzi.Finance.Payroll.Services.Internal;

/// <summary>
/// 工资单计算引擎（圈选员工 → 预取税级/YTD → 按结构行序求值 → 钩子环绕）
/// </summary>
/// <remarks>
/// 纯计算，不写库：返回内存中的 <see cref="Payslip"/>（含行），由 <c>PayRunService</c>
/// 在工作单元内落库。YTD 与税级表预取为单次分组/批量查询（禁 N+1），以闭包喂入
/// <see cref="SalaryFormulaContext"/>。任一员工计算失败以 <c>Payslip.CalculationError</c>
/// 记录，不炸整批；只要批次内存在 Error，过账即被拒绝。
/// </remarks>
public sealed class PayslipCalculator
{
    private readonly IRepository<Employee, Guid> _employeeRepo;
    private readonly IRepository<SalaryAssignment, Guid> _assignmentRepo;
    private readonly IRepository<SalaryStructure, Guid> _structureRepo;
    private readonly IRepository<SalaryComponent, Guid> _componentRepo;
    private readonly IRepository<BracketTable, Guid> _bracketRepo;
    private readonly IRepository<PayRun, Guid> _runRepo;
    private readonly IRepository<Payslip, Guid> _payslipRepo;
    private readonly IRepository<PayslipLine, Guid> _lineRepo;
    private readonly IRepository<PayRunInput, Guid> _inputRepo;
    private readonly IRepository<FiscalYear, Guid> _fiscalYearRepo;
    private readonly ISalaryFormulaEvaluator _evaluator;
    private readonly IReadOnlyList<IPayslipCalculationHook> _hooks;
    private readonly PayrollOptions _payrollOptions;
    private readonly FinanceOptions _financeOptions;
    private readonly ILogger<PayslipCalculator> _logger;

    public PayslipCalculator(
        IRepository<Employee, Guid> employeeRepo,
        IRepository<SalaryAssignment, Guid> assignmentRepo,
        IRepository<SalaryStructure, Guid> structureRepo,
        IRepository<SalaryComponent, Guid> componentRepo,
        IRepository<BracketTable, Guid> bracketRepo,
        IRepository<PayRun, Guid> runRepo,
        IRepository<Payslip, Guid> payslipRepo,
        IRepository<PayslipLine, Guid> lineRepo,
        IRepository<PayRunInput, Guid> inputRepo,
        IRepository<FiscalYear, Guid> fiscalYearRepo,
        ISalaryFormulaEvaluator evaluator,
        IEnumerable<IPayslipCalculationHook> hooks,
        IOptionsSnapshot<PayrollOptions> payrollOptions,
        IOptionsSnapshot<FinanceOptions> financeOptions,
        ILogger<PayslipCalculator> logger)
    {
        _employeeRepo = Check.NotNull(employeeRepo);
        _assignmentRepo = Check.NotNull(assignmentRepo);
        _structureRepo = Check.NotNull(structureRepo);
        _componentRepo = Check.NotNull(componentRepo);
        _bracketRepo = Check.NotNull(bracketRepo);
        _runRepo = Check.NotNull(runRepo);
        _payslipRepo = Check.NotNull(payslipRepo);
        _lineRepo = Check.NotNull(lineRepo);
        _inputRepo = Check.NotNull(inputRepo);
        _fiscalYearRepo = Check.NotNull(fiscalYearRepo);
        _evaluator = Check.NotNull(evaluator);
        _hooks = Check.NotNull(hooks).OrderBy(h => h.Order).ToList();
        _payrollOptions = Check.NotNull(payrollOptions).Value;
        _financeOptions = Check.NotNull(financeOptions).Value;
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 计算发薪批次的工资单。
    /// </summary>
    /// <param name="run">发薪批次（已加载）</param>
    /// <param name="workedDaysOverrides">员工 → 出勤天数覆盖（缺省取周期总天数）</param>
    /// <param name="employeeFilter">仅计算这些员工（null = 全部圈选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task<Result<List<Payslip>>> CalculateAsync(
        PayRun run,
        IReadOnlyDictionary<Guid, decimal>? workedDaysOverrides,
        IReadOnlyCollection<Guid>? employeeFilter,
        CancellationToken cancellationToken)
    {
        Check.NotNull(run);

        var periodStart = run.PeriodStart.ToUtcDate();
        var periodEnd = run.PeriodEnd.ToUtcDate();
        var payDate = run.PayDate.ToUtcDate();

        // 1. 圈选员工：在册 + 有覆盖期间的分配 + 未在期初前离职 + 结构过滤匹配
        var employeeQuery = _employeeRepo.AsNoTracking().Where(e => e.IsActive);
        if (employeeFilter != null)
        {
            var filterIds = employeeFilter.ToList();
            employeeQuery = employeeQuery.Where(e => filterIds.Contains(e.Id));
        }

        var employees = await employeeQuery.ToListAsync(cancellationToken);
        if (employees.Count == 0)
            return Result.Success(new List<Payslip>());

        var employeeIds = employees.Select(e => e.Id).ToList();

        var assignments = await _assignmentRepo.AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId) && a.EffectiveFrom <= periodEnd)
            .ToListAsync(cancellationToken);

        var resolvedAssignment = assignments
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => PayRunEligibility.PickEffective(g, periodEnd));

        // 判据在 PayRunEligibility ——「谁进这个批次」必须与一次性输入的录入端同口径，
        // 两处各写一份的话，漂移的症状是输入被收下、跑批时那个人不在批次里。
        var eligible = new List<(Employee Employee, SalaryAssignment Assignment)>();
        foreach (var employee in employees)
        {
            resolvedAssignment.TryGetValue(employee.Id, out var assignment);
            if (PayRunEligibility.Evaluate(run, employee, assignment) != PayRunEligibilityStatus.Eligible)
                continue;
            eligible.Add((employee, assignment!));
        }

        if (eligible.Count > _payrollOptions.MaxEmployeesPerRun)
        {
            return Result.Failure<List<Payslip>>(
                $"The pay run selects {eligible.Count} employees, exceeding the configured limit of {_payrollOptions.MaxEmployeesPerRun}.", 400);
        }

        if (eligible.Count == 0)
            return Result.Success(new List<Payslip>());

        // 2. 预取结构（含行）、组件、税级表、YTD
        var structureIds = eligible.Select(x => x.Assignment.StructureId).Distinct().ToList();
        var structures = await _structureRepo.AsNoTracking()
            .Include(s => s.Lines)
            .Where(s => structureIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var inputMap = await BuildInputMapAsync(run.Id, cancellationToken);

        // 组件预取覆盖"结构行引用的"∪"一次性输入引用的"——后者可能已经不在结构里了，
        // 而那正是要报出来的情况，报的时候得说得出组件编码。
        var componentIds = structures.Values
            .SelectMany(s => s.Lines.Select(l => l.ComponentId))
            .Concat(inputMap.Values.SelectMany(m => m.Keys))
            .Distinct().ToList();
        var components = await _componentRepo.AsNoTracking()
            .Where(c => componentIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var bracketResolver = await BuildBracketResolverAsync(payDate, cancellationToken);
        var ytdMap = await BuildYtdMapAsync(run, employeeIds, payDate, cancellationToken);

        // 3. 逐员工计算
        var decimals = _financeOptions.BaseCurrencyDecimals;
        var periodDays = (decimal)((periodEnd - periodStart).Days + 1);
        var periodsPerYear = PeriodsPerYear(run.Frequency);

        var payslips = new List<Payslip>(eligible.Count);
        foreach (var (employee, assignment) in eligible)
        {
            if (!structures.TryGetValue(assignment.StructureId, out var structure))
                continue;

            var workedDays = workedDaysOverrides != null && workedDaysOverrides.TryGetValue(employee.Id, out var wd)
                ? wd
                : periodDays;

            var payslip = await ComputeOneAsync(
                run, employee, assignment, structure, components,
                bracketResolver, ytdMap, inputMap, workedDays, periodDays, periodsPerYear, decimals, cancellationToken);
            payslips.Add(payslip);
        }

        return Result.Success(payslips);
    }

    private async Task<Payslip> ComputeOneAsync(
        PayRun run,
        Employee employee,
        SalaryAssignment assignment,
        SalaryStructure structure,
        IReadOnlyDictionary<Guid, SalaryComponent> components,
        Func<string, decimal, decimal> bracketResolver,
        IReadOnlyDictionary<(Guid, string), decimal> ytdMap,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, decimal>> inputMap,
        decimal workedDays,
        decimal periodDays,
        int periodsPerYear,
        int decimals,
        CancellationToken cancellationToken)
    {
        string? error = null;

        // 扩展属性是自由 JSON，且写入口不止管理端（country pack 播种、外部同步、直连 SQL）：
        // 解析失败只该让这一张工资单带错，不该把整批计算炸成 500（有 Error 的批次本就禁止过账）
        IReadOnlyDictionary<string, string> attributes;
        try
        {
            attributes = PayrollAttributeHelper.Parse(employee.AttributesJson);
        }
        catch (Exception ex)
        {
            attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = $"Employee attributes could not be parsed: {ex.Message}";
        }

        var context = new PayslipCalculationContext(run, employee, structure, assignment.BaseAmount)
        {
            Attributes = attributes
        };
        context.Variables[PayrollFormulaVariables.Base] = assignment.BaseAmount;
        context.Variables[PayrollFormulaVariables.Gross] = 0m;
        context.Variables[PayrollFormulaVariables.WorkedDays] = workedDays;
        context.Variables[PayrollFormulaVariables.PeriodDays] = periodDays;
        context.Variables[PayrollFormulaVariables.PeriodsPerYear] = periodsPerYear;

        // 求值上下文按**行**构造：Input() 绑定的是"当前这一行的那一笔一次性输入"。
        // Variables 传的是同一个字典引用，所以逐行写入与钩子注入照旧对后续行可见。
        var ytdResolver = (string code) => ytdMap.GetValueOrDefault((employee.Id, code.Trim().ToUpperInvariant()), 0m);
        SalaryFormulaContext EvalContext(decimal? inputAmount) => new()
        {
            Variables = context.Variables,
            Attributes = attributes,
            BracketResolver = bracketResolver,
            YtdResolver = ytdResolver,
            InputAmount = inputAmount
        };

        inputMap.TryGetValue(employee.Id, out var employeeInputs);

        if (error == null)
        {
            var before = await RunHooksAsync(context, isBefore: true, cancellationToken);
            if (!before.Succeeded)
                error = before.Message;
        }

        if (error == null)
        {
            var gross = 0m;
            foreach (var line in structure.Lines.OrderBy(l => l.Sequence))
            {
                if (!components.TryGetValue(line.ComponentId, out var component))
                {
                    error = $"Salary component '{line.ComponentId}' no longer exists.";
                    break;
                }

                var formula = string.IsNullOrWhiteSpace(line.FormulaOverride) ? component.Formula : line.FormulaOverride;
                var condition = string.IsNullOrWhiteSpace(line.ConditionOverride) ? component.Condition : line.ConditionOverride;

                decimal? inputAmount = employeeInputs != null && employeeInputs.TryGetValue(component.Id, out var entered)
                    ? entered
                    : null;
                var evalContext = EvalContext(inputAmount);

                if (!string.IsNullOrWhiteSpace(condition))
                {
                    var conditionResult = _evaluator.EvaluateCondition(condition, evalContext);
                    if (!conditionResult.Succeeded)
                    {
                        error = $"Component '{component.Code}' condition failed: {conditionResult.Message}";
                        break;
                    }
                    if (!conditionResult.Data)
                    {
                        // ★条件不成立的组件按 0 参与后续引用，而不是让它的变量名不存在：
                        // 结构校验只保证"引用的是更早序号的行"，一个带条件的行被跳过时，
                        // 引用它的后续公式会以「未知变量」失败 → 该员工整张工资单带错 →
                        // 整个批次因此不可过账。0 是唯一可用的语义（公式里没有 if 可自保）。
                        context.Variables[component.Code] = 0m;
                        continue;
                    }
                }

                decimal amount;
                if (line.AmountOverride.HasValue)
                {
                    amount = line.AmountOverride.Value;
                }
                else if (!string.IsNullOrWhiteSpace(formula))
                {
                    var amountResult = _evaluator.Evaluate(formula, evalContext);
                    if (!amountResult.Succeeded)
                    {
                        error = $"Component '{component.Code}' formula failed: {amountResult.Message}";
                        break;
                    }
                    amount = amountResult.Data;
                }
                else
                {
                    amount = component.DefaultAmount ?? 0m;
                }

                // ★备注项是**具名中间量**，不是钱：把它舍到货币精度，等于说 Round(expr, 2)
                // 与 expr 可以互换——而"可互换"恰恰是这个类型值得存在的**唯一**理由
                // （不然那条子表达式只能在后续每一处原文重抄一遍）。
                // 实测：一条联邦 + 省两级的所得税链路，中间量舍到 2 位时，20,243 个场景里有 485 个
                // 与"整条内联成一个表达式"的结果差 1 分；不舍则 20,243 个逐个相同。
                // 法规要求在某一步舍入时，公式里显式写 Round(x, 2)——白名单函数，看得见、可核对。
                // 收入/扣减/雇主承担三类仍必须舍：它们是要进合计、进分录、印在工资条上的钱。
                if (component.Type != SalaryComponentType.Informational)
                    amount = Math.Round(amount, decimals, MidpointRounding.AwayFromZero);
                // 备注项参与不了任何合计，所以负数在它身上产生不了荒谬的净额，
                // 而具名中间量（抵免、冲回）本来就是带符号的。其余三类仍必须为正：
                // 一个负的扣减项就是一次没人申报的加薪。
                if (amount < 0 && component.Type != SalaryComponentType.Informational)
                {
                    error = $"Component '{component.Code}' produced a negative amount ({amount}).";
                    break;
                }

                context.Variables[component.Code] = amount;
                if (component.Type == SalaryComponentType.Earning)
                {
                    gross += amount;
                    context.Variables[PayrollFormulaVariables.Gross] = gross;
                }

                // 逐行 YTD = 本组件历史已提交批次的累计（预取的 ytdMap，按 Code 大写归一）+ 本期额。
                var priorYtd = ytdMap.GetValueOrDefault((employee.Id, component.Code.Trim().ToUpperInvariant()), 0m);

                context.Lines.Add(new PayslipLine
                {
                    Sequence = line.Sequence,
                    ComponentId = component.Id,
                    ComponentCode = component.Code,
                    ComponentName = component.Name,
                    ComponentType = component.Type,
                    Amount = amount,
                    YtdAmount = priorYtd + amount,
                    InputAmount = inputAmount,
                    FormulaSnapshot = line.AmountOverride.HasValue ? null : (string.IsNullOrWhiteSpace(formula) ? null : formula),
                    ExpenseAccountId = component.ExpenseAccountId,
                    LiabilityAccountId = component.LiabilityAccountId
                });
            }
        }

        if (error == null)
        {
            var after = await RunHooksAsync(context, isBefore: false, cancellationToken);
            if (!after.Succeeded)
                error = after.Message;
        }

        var grossPay = context.Lines.Where(l => l.ComponentType == SalaryComponentType.Earning).Sum(l => l.Amount);
        var deductions = context.Lines.Where(l => l.ComponentType == SalaryComponentType.Deduction).Sum(l => l.Amount);
        var employerCost = context.Lines.Where(l => l.ComponentType == SalaryComponentType.EmployerContribution).Sum(l => l.Amount);
        var netPay = grossPay - deductions;

        if (error == null && employeeInputs is { Count: > 0 })
            error = DescribeOrphanedInputs(employeeInputs, structure, components);

        if (error == null && netPay < 0)
            error = $"Net pay is negative ({netPay}); deductions exceed gross earnings.";

        var payslip = new Payslip
        {
            PayRunId = run.Id,
            EmployeeId = employee.Id,
            EmployeeCode = employee.Code,
            EmployeeName = employee.Name,
            StructureId = structure.Id,
            BaseAmount = assignment.BaseAmount,
            PeriodDays = periodDays,
            WorkedDays = workedDays,
            GrossPay = grossPay,
            TotalDeductions = deductions,
            EmployerCost = employerCost,
            NetPay = netPay,
            CalculationError = error
        };
        foreach (var line in context.Lines)
            payslip.Lines.Add(line);

        return payslip;
    }

    private async Task<Result> RunHooksAsync(PayslipCalculationContext context, bool isBefore, CancellationToken cancellationToken)
    {
        foreach (var hook in _hooks)
        {
            var result = isBefore
                ? await hook.BeforeCalculateAsync(context, cancellationToken)
                : await hook.AfterCalculateAsync(context, cancellationToken);
            if (!result.Succeeded)
                return result;
        }
        return Result.Success();
    }

    /// <summary>
    /// 预取当日生效的全部税级表（按 Code 取 EffectiveFrom ≤ PayDate 的最大版本），
    /// 以闭包提供 Bracket(code, amount)。
    /// </summary>
    private async Task<Func<string, decimal, decimal>> BuildBracketResolverAsync(DateTime payDate, CancellationToken cancellationToken)
    {
        var tables = await _bracketRepo.AsNoTracking()
            .Include(t => t.Rows)
            .Where(t => t.IsActive && t.EffectiveFrom <= payDate)
            .ToListAsync(cancellationToken);

        var resolved = tables
            .GroupBy(t => t.Code)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(t => t.EffectiveFrom).First().Rows
                    .OrderBy(r => r.Sequence)
                    .Select(r => r.MapTo<BracketRowDto>())
                    .ToList());

        return (code, amount) =>
        {
            var key = code.Trim().ToUpperInvariant();
            if (!resolved.TryGetValue(key, out var rows))
                throw new InvalidOperationException($"No active bracket table '{key}' is effective on {payDate:yyyy-MM-dd}.");
            return BracketMath.Calculate(rows, amount);
        };
    }

    /// <summary>
    /// 预取本年度内已过账（或 OpeningBalance）payslip 的各组件累计（单次分组查询）。
    /// </summary>
    private async Task<IReadOnlyDictionary<(Guid, string), decimal>> BuildYtdMapAsync(
        PayRun run, List<Guid> employeeIds, DateTime payDate, CancellationToken cancellationToken)
    {
        var windowStart = await ResolveYtdWindowStartAsync(payDate, cancellationToken);

        var rows = await (
            from line in _lineRepo.AsNoTracking()
            join slip in _payslipRepo.AsNoTracking() on line.PayslipId equals slip.Id
            join r in _runRepo.AsNoTracking() on slip.PayRunId equals r.Id
            where employeeIds.Contains(slip.EmployeeId)
                && r.Id != run.Id
                // YTD 累计须含所有已提交批次：Posted 之后的正常状态推进（PartiallyPaid/Paid）不得掉出，
                // 否则首次付款后法定上限（SS/CPP/EI）与累计所得税的 Ytd() 基数归零、上限永不封顶。
                // Voided/Draft/Calculated 仍排除（保留作废冲销与草稿不入账语义）。
                && (r.Status == PayRunStatus.Posted
                    || r.Status == PayRunStatus.PartiallyPaid
                    || r.Status == PayRunStatus.Paid
                    || r.Source == PayRunSource.OpeningBalance)
                && r.PayDate >= windowStart && r.PayDate <= payDate
            group line by new { slip.EmployeeId, line.ComponentCode, line.ComponentType } into g
            select new { g.Key.EmployeeId, g.Key.ComponentCode, g.Key.ComponentType, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<(Guid, string), decimal>();
        foreach (var row in rows)
        {
            var key = (row.EmployeeId, row.ComponentCode);
            map[key] = map.GetValueOrDefault(key) + row.Total;

            // 按类型的聚合键与逐组件累计**同一次查询**得出：Ytd('#GROSS') 之类的上限基数
            // 不该是第二次往返，也不该由调用方在公式里把收入项逐个列全（漏一个就静默少扣）。
            var aggregate = row.ComponentType switch
            {
                SalaryComponentType.Earning => PayrollYtdAggregates.Gross,
                SalaryComponentType.Deduction => PayrollYtdAggregates.Deductions,
                SalaryComponentType.EmployerContribution => PayrollYtdAggregates.EmployerCost,
                // Informational 不进任何合计——这正是它的定义。
                _ => null
            };
            if (aggregate == null)
                continue;

            var aggKey = (row.EmployeeId, aggregate);
            map[aggKey] = map.GetValueOrDefault(aggKey) + row.Total;

            if (row.ComponentType is SalaryComponentType.Earning or SalaryComponentType.Deduction)
            {
                var netKey = (row.EmployeeId, PayrollYtdAggregates.Net);
                var signed = row.ComponentType == SalaryComponentType.Earning ? row.Total : -row.Total;
                map[netKey] = map.GetValueOrDefault(netKey) + signed;
            }
        }

        return map;
    }

    /// <summary>
    /// 预取本批次的全部一次性输入（单次查询），按 员工 → (组件 → 金额) 喂给 <c>Input()</c>。
    /// </summary>
    /// <remarks>
    /// 键用 ComponentId 而不是 Code：<c>Input()</c> 绑定"当前这一行的组件"，不经名字解析，
    /// 所以组件改编码不会让一笔已批准的奖金对不上号。★按员工分组而不是拍平成 (员工, 组件)
    /// 复合键，是因为算完之后还要**反过来**遍历这个员工的全部输入，找出没人读的那些。
    /// </remarks>
    private async Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, decimal>>> BuildInputMapAsync(
        Guid runId, CancellationToken cancellationToken)
    {
        var rows = await _inputRepo.AsNoTracking()
            .Where(i => i.PayRunId == runId)
            .Select(i => new { i.EmployeeId, i.ComponentId, i.Amount })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<Guid, IReadOnlyDictionary<Guid, decimal>>();
        foreach (var row in rows)
        {
            if (!map.TryGetValue(row.EmployeeId, out var byComponent))
            {
                byComponent = new Dictionary<Guid, decimal>();
                map[row.EmployeeId] = byComponent;
            }
            ((Dictionary<Guid, decimal>)byComponent)[row.ComponentId] = row.Amount;
        }
        return map;
    }

    /// <summary>
    /// 本期为这个员工录了、但结构里没有任何一行会读它的一次性输入 —— 有则给出错误文案。
    /// </summary>
    /// <remarks>
    /// 录入端已经逐条挡过一次，但那是**录入那一刻**的事实。之后改组件公式、改结构行、
    /// 钉上 <c>AmountOverride</c>、把员工换到别的结构，任何一条都能让那笔钱重新变成孤儿。
    /// 出口再核一次，落进 <c>CalculationError</c> —— 有错的批次过不了账，于是"少发一笔已批准
    /// 的奖金"从静默错账变成一条挡在过账前面的消息。判据与录入端同源（<see cref="PayRunInputBinding"/>）。
    /// </remarks>
    private string? DescribeOrphanedInputs(
        IReadOnlyDictionary<Guid, decimal> employeeInputs,
        SalaryStructure structure,
        IReadOnlyDictionary<Guid, SalaryComponent> components)
    {
        var orphans = new List<string>();
        foreach (var (componentId, amount) in employeeInputs)
        {
            if (!components.TryGetValue(componentId, out var component))
            {
                orphans.Add($"{amount} (component {componentId} no longer exists)");
                continue;
            }

            var line = structure.Lines.FirstOrDefault(l => l.ComponentId == componentId);
            var binding = PayRunInputBinding.Evaluate(_evaluator, line, component);
            if (binding == PayRunInputBindingStatus.Readable)
                continue;

            var reason = binding switch
            {
                PayRunInputBindingStatus.NotOnStructure => "the component is no longer part of this employee's salary structure",
                PayRunInputBindingStatus.PinnedAmount => "the structure line pins a fixed amount that takes precedence over the formula",
                _ => $"the effective formula no longer calls {PayrollFormulaFunctions.Input}()"
            };
            orphans.Add($"{component.Code} ({amount}): {reason}");
        }

        return orphans.Count == 0
            ? null
            : "One-time inputs entered for this pay run would be ignored - " + string.Join("; ", orphans) +
              ". Fix the salary structure or remove the inputs, then recalculate.";
    }

    private async Task<DateTime> ResolveYtdWindowStartAsync(DateTime payDate, CancellationToken cancellationToken)
    {
        var calendarStart = new DateTime(payDate.Year, 1, 1).ToUtcDate();
        if (_payrollOptions.YtdBasis != YtdBasis.FiscalYear)
            return calendarStart;

        var fiscalYear = await _fiscalYearRepo.AsNoTracking()
            .Where(f => f.StartDate <= payDate && f.EndDate >= payDate)
            .OrderByDescending(f => f.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (fiscalYear != null)
            return fiscalYear.StartDate.ToUtcDate();

        _logger.LogWarning(
            "YtdBasis is FiscalYear but no fiscal year covers pay date {PayDate:yyyy-MM-dd}; falling back to the calendar year for YTD aggregation.",
            payDate);
        return calendarStart;
    }

    private static int PeriodsPerYear(PayFrequency frequency) => frequency switch
    {
        PayFrequency.Monthly => 12,
        PayFrequency.SemiMonthly => 24,
        PayFrequency.BiWeekly => 26,
        PayFrequency.Weekly => 52,
        _ => 12
    };
}
