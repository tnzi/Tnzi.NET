namespace Tnzi.Finance.Payroll.Services;

/// <summary>
/// 发薪批次服务的外部摄取切面（External / OpeningBalance 两条路径）
/// </summary>
/// <remarks>
/// 幂等以 ProviderRunId 承载（先查 + 唯一索引兜底返回赢家）；External 且
/// <see cref="PayrollOptions.ExternalAutoPost"/> 时无错自动过账，半完成状态在
/// 下次同 ProviderRunId 摄取时自愈；OpeningBalance 只供 Ytd() 聚合、不入总账。
/// </remarks>
public partial class PayRunService
{
    public async Task<Result<PayRunDto>> CreateFromExternalAsync(ExternalPayRunIngestDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        if (string.IsNullOrWhiteSpace(input.ProviderRunId))
            return Fail<PayRunDto>("ProviderRunId is required for external ingestion.", 400);
        if (input.Source is not (PayRunSource.External or PayRunSource.OpeningBalance))
            return Fail<PayRunDto>("External ingestion source must be External or OpeningBalance.", 400);
        if (input.Payslips == null || input.Payslips.Count == 0)
            return Fail<PayRunDto>("At least one payslip is required.", 400);

        var providerRunId = input.ProviderRunId.Trim();

        // 幂等：已存在则返回赢家（External + AutoPost 且 Calculated 无错则自愈过账）
        var existing = await _runRepo.AsNoTracking().FirstOrDefaultAsync(r => r.ProviderRunId == providerRunId, cancellationToken);
        if (existing != null)
        {
            await SelfHealPostAsync(existing, cancellationToken);
            return await GetAsync(existing.Id, cancellationToken);
        }

        // 解析组件与员工
        var componentCodes = input.Payslips
            .SelectMany(p => p.Lines ?? [])
            .Select(l => l.ComponentCode?.Trim().ToUpperInvariant())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct().ToList();
        var components = await _componentRepo.AsNoTracking()
            .Where(c => componentCodes.Contains(c.Code))
            .ToDictionaryAsync(c => c.Code, cancellationToken);

        var employeeCodes = input.Payslips.Select(p => p.EmployeeCode?.Trim()).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        var employees = await _employeeRepo.AsNoTracking()
            .Where(e => employeeCodes.Contains(e.Code))
            .ToDictionaryAsync(e => e.Code, cancellationToken);

        var buildResult = BuildExternalRun(input, providerRunId, components, employees);
        if (!buildResult.Succeeded)
            return Fail<PayRunDto>(buildResult.Message!, buildResult.Code ?? 400);

        var run = buildResult.Data!;

        try
        {
            await _runRepo.InsertAsync(run, cancellationToken);
            await _runRepo.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            // 并发摄取：返回赢家
            var winner = await _runRepo.AsNoTracking().FirstOrDefaultAsync(r => r.ProviderRunId == providerRunId, cancellationToken);
            if (winner != null)
            {
                await SelfHealPostAsync(winner, cancellationToken);
                return await GetAsync(winner.Id, cancellationToken);
            }
            return Fail<PayRunDto>("Failed to ingest the external pay run.", 500);
        }

        await PublishEventAsync(new PayRunCalculatedEvent
        {
            PayRunId = run.Id,
            EmployeeCount = run.EmployeeCount,
            ErrorCount = run.Payslips.Count(p => p.CalculationError != null),
            GrossTotal = run.GrossTotal,
            NetTotal = run.NetTotal
        }, cancellationToken);

        await SelfHealPostAsync(run, cancellationToken);
        return await GetAsync(run.Id, cancellationToken);
    }

    private async Task SelfHealPostAsync(PayRun run, CancellationToken cancellationToken)
    {
        if (run.Source != PayRunSource.External || !_payrollOptions.ExternalAutoPost || run.Status != PayRunStatus.Calculated)
            return;

        var errorCount = await _payslipRepo.CountAsync(p => p.PayRunId == run.Id && p.CalculationError != null, cancellationToken);
        if (errorCount > 0)
            return;

        var posted = await PostAsync(run.Id, cancellationToken);
        if (!posted.Succeeded)
        {
            _logger.LogWarning(
                "Auto-post of external pay run {PayRunId} failed ({Message}); it remains in Calculated state and will self-heal on the next ingestion.",
                run.Id, posted.Message);
        }
    }

    private Result<PayRun> BuildExternalRun(
        ExternalPayRunIngestDto input,
        string providerRunId,
        IReadOnlyDictionary<string, SalaryComponent> components,
        IReadOnlyDictionary<string, Employee> employees)
    {
        if (!Enum.IsDefined(input.Frequency))
            return Result.Failure<PayRun>("Frequency must be Monthly, SemiMonthly, BiWeekly or Weekly.", 400);
        if (input.Payslips.Count > _payrollOptions.MaxEmployeesPerRun)
            return Result.Failure<PayRun>($"An external pay run cannot exceed {_payrollOptions.MaxEmployeesPerRun} employees (got {input.Payslips.Count}); split it into multiple runs.", 400);
        var decimals = _financeOptions.BaseCurrencyDecimals;
        var periodStart = input.PeriodStart.ToUtcDate();
        var periodEnd = input.PeriodEnd.ToUtcDate();
        if (periodEnd < periodStart)
            return Result.Failure<PayRun>("PeriodEnd must be on or after PeriodStart.", 400);
        var periodDays = (decimal)((periodEnd - periodStart).Days + 1);

        var run = new PayRun
        {
            Status = PayRunStatus.Calculated,
            Source = input.Source,
            ProviderRunId = providerRunId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            PayDate = input.PayDate.ToUtcDate(),
            Frequency = input.Frequency,
            Memo = input.Memo
        };

        var payslips = new List<Payslip>();
        foreach (var ext in input.Payslips)
        {
            var employeeCode = ext.EmployeeCode?.Trim();
            if (string.IsNullOrWhiteSpace(employeeCode) || !employees.TryGetValue(employeeCode, out var employee))
                return Result.Failure<PayRun>($"Employee '{ext.EmployeeCode}' is not registered; create the employee before ingesting.", 400);
            if (ext.Lines == null || ext.Lines.Count == 0)
                return Result.Failure<PayRun>($"Payslip for employee '{employeeCode}' has no lines.", 400);

            var payslip = new Payslip
            {
                EmployeeId = employee.Id,
                EmployeeCode = employee.Code,
                EmployeeName = employee.Name,
                StructureId = Guid.Empty,
                BaseAmount = 0m,
                PeriodDays = periodDays,
                WorkedDays = ext.WorkedDays ?? periodDays
            };

            var gross = 0m;
            var deductions = 0m;
            var employerCost = 0m;
            var sequence = 1;
            foreach (var extLine in ext.Lines)
            {
                var code = extLine.ComponentCode?.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(code) || !components.TryGetValue(code, out var component))
                    return Result.Failure<PayRun>($"Salary component '{extLine.ComponentCode}' is not registered; seed the component (or country pack) before ingesting.", 400);

                var amount = Math.Round(extLine.Amount, decimals, MidpointRounding.AwayFromZero);
                payslip.Lines.Add(new PayslipLine
                {
                    Sequence = sequence++,
                    ComponentId = component.Id,
                    ComponentCode = component.Code,
                    ComponentName = component.Name,
                    ComponentType = component.Type,
                    Amount = amount,
                    ExpenseAccountId = component.ExpenseAccountId,
                    LiabilityAccountId = component.LiabilityAccountId
                });

                switch (component.Type)
                {
                    case SalaryComponentType.Earning: gross += amount; break;
                    case SalaryComponentType.Deduction: deductions += amount; break;
                    case SalaryComponentType.EmployerContribution: employerCost += amount; break;
                }
            }

            payslip.GrossPay = gross;
            payslip.TotalDeductions = deductions;
            payslip.EmployerCost = employerCost;
            payslip.NetPay = gross - deductions;
            if (payslip.NetPay < 0)
                payslip.CalculationError = $"Net pay is negative ({payslip.NetPay}); ingested deductions exceed earnings.";

            run.Payslips.Add(payslip);
            payslips.Add(payslip);
        }

        ApplyAggregates(run, payslips);
        return Result.Success(run);
    }
}
