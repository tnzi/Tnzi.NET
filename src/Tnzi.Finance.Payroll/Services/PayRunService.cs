namespace Tnzi.Finance.Payroll.Services;

/// <summary>
/// 发薪批次服务（草稿 CRUD + 计算 → 过账 → 付款 → 作废 + 外部摄取）
/// </summary>
/// <remarks>
/// 过账/付款/作废经 Finance 的 <see cref="ILedgerPostingService"/> 扩展面投影总账；
/// 多凭证循环的中途失败以 <see cref="Internal.PayrollUnitOfWorkAbortException"/> 传递
/// （UoW 只在异常时回滚，返回失败 Result 仍会部分提交）；凭证号在最后可失败步骤之后分配。
/// </remarks>
public partial class PayRunService : ApplicationService, IPayRunService
{
    private readonly IRepository<PayRun, Guid> _runRepo;
    private readonly IRepository<Payslip, Guid> _payslipRepo;
    private readonly IRepository<PayslipLine, Guid> _lineRepo;
    private readonly IRepository<SalaryStructure, Guid> _structureRepo;
    private readonly IRepository<SalaryComponent, Guid> _componentRepo;
    private readonly IRepository<Employee, Guid> _employeeRepo;
    private readonly ILedgerPostingService _ledgerPosting;
    private readonly IDocumentNumberService _documentNumber;
    private readonly PayslipCalculator _calculator;
    private readonly PayrollPostingHelper _postingHelper;
    private readonly PayrollOptions _payrollOptions;
    private readonly FinanceOptions _financeOptions;
    private readonly ILogger<PayRunService> _logger;

    private const string NumberScope = "PayRun";

    public PayRunService(
        IServiceProvider serviceProvider,
        IRepository<PayRun, Guid> runRepo,
        IRepository<Payslip, Guid> payslipRepo,
        IRepository<PayslipLine, Guid> lineRepo,
        IRepository<SalaryStructure, Guid> structureRepo,
        IRepository<SalaryComponent, Guid> componentRepo,
        IRepository<Employee, Guid> employeeRepo,
        ILedgerPostingService ledgerPosting,
        IDocumentNumberService documentNumber,
        PayslipCalculator calculator,
        PayrollPostingHelper postingHelper,
        IOptionsSnapshot<PayrollOptions> payrollOptions,
        IOptionsSnapshot<FinanceOptions> financeOptions,
        ILogger<PayRunService> logger) : base(serviceProvider)
    {
        _runRepo = Check.NotNull(runRepo);
        _payslipRepo = Check.NotNull(payslipRepo);
        _lineRepo = Check.NotNull(lineRepo);
        _structureRepo = Check.NotNull(structureRepo);
        _componentRepo = Check.NotNull(componentRepo);
        _employeeRepo = Check.NotNull(employeeRepo);
        _ledgerPosting = Check.NotNull(ledgerPosting);
        _documentNumber = Check.NotNull(documentNumber);
        _calculator = Check.NotNull(calculator);
        _postingHelper = Check.NotNull(postingHelper);
        _payrollOptions = Check.NotNull(payrollOptions).Value;
        _financeOptions = Check.NotNull(financeOptions).Value;
        _logger = Check.NotNull(logger);
    }

    // ── 查询 ────────────────────────────────────────────────────────────────

    public async Task<Result<IPagedList<PayRunListDto>>> GetPagedAsync(PayRunQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _runRepo.AsNoTracking()
            .Filter(query)
            .OrderByDescending(r => r.PayDate).ThenByDescending(r => r.CreationTime)
            .ProjectTo<PayRun, PayRunListDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<PayRunDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var run = await _runRepo.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run == null)
            return Fail<PayRunDto>("Pay run not found.", 404);

        return Ok(await ToDtoAsync(run, cancellationToken));
    }

    // ── 草稿 CRUD ─────────────────────────────────────────────────────────────

    public async Task<Result<PayRunDto>> CreateAsync(CreatePayRunDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var validation = await ValidateDraftAsync(input, cancellationToken);
        if (!validation.Succeeded)
            return Fail<PayRunDto>(validation.Message ?? "Invalid pay run.", validation.Code ?? 400);

        var run = new PayRun
        {
            Status = PayRunStatus.Draft,
            Source = PayRunSource.Internal,
            PeriodStart = input.PeriodStart.ToUtcDate(),
            PeriodEnd = input.PeriodEnd.ToUtcDate(),
            PayDate = input.PayDate.ToUtcDate(),
            Frequency = input.Frequency,
            StructureId = input.StructureId,
            Memo = input.Memo
        };

        await _runRepo.InsertAsync(run, cancellationToken);
        await _runRepo.SaveChangesAsync(cancellationToken);

        return Ok(await ToDtoAsync(run, cancellationToken));
    }

    public async Task<Result<PayRunDto>> UpdateAsync(Guid id, UpdatePayRunDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var run = await _runRepo.GetAsync(id, cancellationToken);
        if (run == null)
            return Fail<PayRunDto>("Pay run not found.", 404);
        if (run.Status != PayRunStatus.Draft)
            return Fail<PayRunDto>("Only a draft pay run can be edited.", 409);

        var validation = await ValidateDraftAsync(input, cancellationToken);
        if (!validation.Succeeded)
            return Fail<PayRunDto>(validation.Message ?? "Invalid pay run.", validation.Code ?? 400);

        run.PeriodStart = input.PeriodStart.ToUtcDate();
        run.PeriodEnd = input.PeriodEnd.ToUtcDate();
        run.PayDate = input.PayDate.ToUtcDate();
        run.Frequency = input.Frequency;
        run.StructureId = input.StructureId;
        run.Memo = input.Memo;

        await _runRepo.UpdateAsync(run, cancellationToken);
        await _runRepo.SaveChangesAsync(cancellationToken);

        return Ok(await ToDtoAsync(run, cancellationToken));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var run = await _runRepo.GetAsync(id, cancellationToken);
        if (run == null)
            return Fail("Pay run not found.", 404);
        if (run.Status != PayRunStatus.Draft)
            return Fail("Only a draft pay run can be deleted. Void a posted run instead.", 409);

        var payslips = await _payslipRepo.AsQueryable(true).Include(p => p.Lines)
            .Where(p => p.PayRunId == id).ToListAsync(cancellationToken);

        try
        {
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                await DeletePayslipsAsync(payslips, ct);
                await _runRepo.DeleteAsync(run, ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 「只有草稿能删」是 check-then-act：读到 Draft 之后、删除落库之前，
            // 另一个请求可能已经把它过账了。`PayRun` 的并发戳会让 DELETE 影响 0 行，
            // 于是删除被正确挡住 —— 但不接住这个异常就成 500，而它其实是 409。
            // Post/Pay/Void/UpdatePayslipInputs 四处都接了，这里漏了。
            return Fail("The pay run was modified concurrently. Reload and try again.", 409);
        }

        return Ok();
    }

    // ── 计算 ────────────────────────────────────────────────────────────────

    public async Task<Result<PayRunDto>> CalculateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var run = await _runRepo.AsQueryable(true).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run == null)
            return Fail<PayRunDto>("Pay run not found.", 404);
        if (run.Source != PayRunSource.Internal)
            return Fail<PayRunDto>("Only internal pay runs are calculated; external and opening-balance runs are ingested.", 409);
        if (run.Status is not (PayRunStatus.Draft or PayRunStatus.Calculated))
            return Fail<PayRunDto>("Only a draft or already-calculated pay run can be (re)calculated.", 409);

        var calcResult = await _calculator.CalculateAsync(run, workedDaysOverrides: null, employeeFilter: null, cancellationToken);
        if (!calcResult.Succeeded)
            return Fail<PayRunDto>(calcResult.Message ?? "Calculation failed.", calcResult.Code ?? 400);

        var payslips = calcResult.Data!;
        if (payslips.Count == 0)
            return Fail<PayRunDto>("No employees are eligible for this pay run (check active status, assignments and the structure filter).", 400);

        var oldPayslips = await _payslipRepo.AsQueryable(true).Include(p => p.Lines)
            .Where(p => p.PayRunId == id).ToListAsync(cancellationToken);

        ApplyAggregates(run, payslips);
        run.Status = PayRunStatus.Calculated;

        try
        {
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                // 先删旧（含 flush）再插新，避免软删旧行与新行在过滤唯一索引上的瞬时冲突
                await DeletePayslipsAsync(oldPayslips, ct);
                await _payslipRepo.SaveChangesAsync(ct);

                await _payslipRepo.InsertManyAsync(payslips, ct);
                await _runRepo.UpdateAsync(run, ct);
                await _runRepo.SaveChangesAsync(ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 与 DeleteAsync 同一形态：状态门是 check-then-act，两个并发 calculate
            // （或 calculate 撞上 post）由并发戳挡住 —— 但那是 409 不是 500。
            return Fail<PayRunDto>("The pay run was modified concurrently. Reload and try again.", 409);
        }

        await PublishEventAsync(new PayRunCalculatedEvent
        {
            PayRunId = run.Id,
            EmployeeCount = payslips.Count,
            ErrorCount = payslips.Count(p => p.CalculationError != null),
            GrossTotal = run.GrossTotal,
            NetTotal = run.NetTotal
        }, cancellationToken);

        return Ok(await ToDtoAsync(run, cancellationToken));
    }

    // ── 过账 ────────────────────────────────────────────────────────────────

    public async Task<Result<PayRunDto>> PostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var run = await _runRepo.AsQueryable(true).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run == null)
            return Fail<PayRunDto>("Pay run not found.", 404);
        if (run.Source == PayRunSource.OpeningBalance)
            return Fail<PayRunDto>("Opening-balance runs are not posted to the general ledger; they only feed YTD aggregation.", 409);
        if (run.Status != PayRunStatus.Calculated)
            return Fail<PayRunDto>("Only a calculated pay run can be posted.", 409);

        var payslips = await _payslipRepo.AsQueryable(true).Include(p => p.Lines)
            .Where(p => p.PayRunId == id).ToListAsync(cancellationToken);
        if (payslips.Count == 0)
            return Fail<PayRunDto>("The pay run has no payslips to post.", 400);
        if (payslips.Any(p => p.CalculationError != null))
            return Fail<PayRunDto>("The pay run has payslips with calculation errors; resolve them before posting.", 400);

        var accountCheck = PayrollPostingHelper.ValidatePostingAccounts(payslips);
        if (!accountCheck.Succeeded)
            return Fail<PayRunDto>(accountCheck.Message!, accountCheck.Code ?? 400);

        var wagesResult = await _postingHelper.ResolveWagesPayableAsync(cancellationToken);
        if (!wagesResult.Succeeded)
            return Fail<PayRunDto>(wagesResult.Message!, wagesResult.Code ?? 400);

        var chunksResult = _postingHelper.BuildPostingChunks(run, payslips, wagesResult.Data!.Id);
        if (!chunksResult.Succeeded)
            return Fail<PayRunDto>(chunksResult.Message!, chunksResult.Code ?? 400);

        var chunks = chunksResult.Data!;

        try
        {
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                foreach (var chunk in chunks)
                {
                    var posted = await _ledgerPosting.PostAsync(chunk.Request, ct);
                    if (!posted.Succeeded)
                        throw new PayrollUnitOfWorkAbortException(Result.Failure(posted.Message ?? "Posting failed.", posted.Code ?? 400));

                    foreach (var payslip in chunk.Payslips)
                        payslip.JournalEntryId = posted.Data!.Id;
                }

                // 凭证号在所有过账（最后可失败步骤）之后分配，失败不烧号
                run.Number = await _documentNumber.NextFormattedAsync(
                    NumberScope, _payrollOptions.PayRunNumberPrefix, _financeOptions.JournalNumberPadding, ct);
                run.Status = PayRunStatus.Posted;

                await _payslipRepo.UpdateManyAsync(payslips, ct);
                await _runRepo.UpdateAsync(run, ct);
                await _runRepo.SaveChangesAsync(ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (PayrollUnitOfWorkAbortException ex)
        {
            return Fail<PayRunDto>(ex.Result.Message ?? "Posting failed.", ex.Result.Code ?? 400);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<PayRunDto>("The pay run was modified concurrently. Reload and try again.", 409);
        }

        await PublishEventAsync(new FinanceDocumentPostedEvent
        {
            DocType = PayrollPostingHelper.PayRunSourceType,
            DocId = run.Id,
            Number = run.Number ?? string.Empty,
            JournalEntryId = payslips.First().JournalEntryId ?? Guid.Empty,
            DocDate = run.PayDate,
            Total = run.GrossTotal
        }, cancellationToken);

        return Ok(await ToDtoAsync(run, cancellationToken));
    }

    // ── 付款 ────────────────────────────────────────────────────────────────

    public async Task<Result<PayRunDto>> PayAsync(Guid id, PayRunPaymentDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        if (input.PaymentDate == default)
            return Fail<PayRunDto>("PaymentDate is required.", 400);

        var run = await _runRepo.AsQueryable(true).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run == null)
            return Fail<PayRunDto>("Pay run not found.", 404);
        if (run.Source == PayRunSource.OpeningBalance)
            return Fail<PayRunDto>("Opening-balance runs are not paid.", 409);
        if (run.Status is not (PayRunStatus.Posted or PayRunStatus.PartiallyPaid))
            return Fail<PayRunDto>("Only a posted or partially-paid pay run can be paid.", 409);

        var allPayslips = await _payslipRepo.AsQueryable(true)
            .Where(p => p.PayRunId == id).ToListAsync(cancellationToken);

        var selected = allPayslips.Where(p => p.PaymentStatus == PayslipPaymentStatus.Unpaid);
        if (input.EmployeeIds is { Count: > 0 })
        {
            var ids = input.EmployeeIds.ToHashSet();
            selected = selected.Where(p => ids.Contains(p.EmployeeId));
        }
        var selectedList = selected.ToList();
        if (selectedList.Count == 0)
            return Fail<PayRunDto>("No matching unpaid payslips to pay.", 400);

        var accountResult = await _postingHelper.ValidatePaymentAccountAsync(input.PaymentAccountId, cancellationToken);
        if (!accountResult.Succeeded)
            return Fail<PayRunDto>(accountResult.Message!, accountResult.Code ?? 400);

        var wagesResult = await _postingHelper.ResolveWagesPayableAsync(cancellationToken);
        if (!wagesResult.Succeeded)
            return Fail<PayRunDto>(wagesResult.Message!, wagesResult.Code ?? 400);

        var payable = selectedList.Where(p => p.NetPay > 0m).ToList();
        var chunks = _postingHelper.BuildPaymentChunks(
            run, payable, wagesResult.Data!.Id, accountResult.Data!.Id, input.PaymentDate);

        try
        {
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                foreach (var chunk in chunks)
                {
                    var request = chunk.Request;
                    request.Memo = string.IsNullOrWhiteSpace(input.Reference) ? request.Memo : $"{request.Memo} ({input.Reference})";

                    var posted = await _ledgerPosting.PostAsync(request, ct);
                    if (!posted.Succeeded)
                        throw new PayrollUnitOfWorkAbortException(Result.Failure(posted.Message ?? "Payment posting failed.", posted.Code ?? 400));

                    foreach (var payslip in chunk.Payslips)
                    {
                        payslip.PaymentJournalEntryId = posted.Data!.Id;
                        payslip.PaymentStatus = PayslipPaymentStatus.Paid;
                        payslip.PaymentMethod = input.PaymentMethod;
                    }
                }

                // 零净额工资单无凭证，直接标记已付
                foreach (var payslip in selectedList.Where(p => p.NetPay <= 0m))
                {
                    payslip.PaymentStatus = PayslipPaymentStatus.Paid;
                    payslip.PaymentMethod = input.PaymentMethod;
                }

                run.Status = allPayslips.All(p => p.PaymentStatus == PayslipPaymentStatus.Paid)
                    ? PayRunStatus.Paid
                    : PayRunStatus.PartiallyPaid;

                await _payslipRepo.UpdateManyAsync(selectedList, ct);
                await _runRepo.UpdateAsync(run, ct);
                await _runRepo.SaveChangesAsync(ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (PayrollUnitOfWorkAbortException ex)
        {
            return Fail<PayRunDto>(ex.Result.Message ?? "Payment failed.", ex.Result.Code ?? 400);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<PayRunDto>("The pay run was modified concurrently. Reload and try again.", 409);
        }

        await PublishEventAsync(new PayRunPaidEvent
        {
            PayRunId = run.Id,
            PaidEmployeeCount = selectedList.Count,
            PaidAmount = payable.Sum(p => p.NetPay),
            PaymentAccountId = input.PaymentAccountId,
            FullyPaid = run.Status == PayRunStatus.Paid
        }, cancellationToken);

        return Ok(await ToDtoAsync(run, cancellationToken));
    }

    // ── 作废 ────────────────────────────────────────────────────────────────

    public async Task<Result<PayRunDto>> VoidAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var run = await _runRepo.AsQueryable(true).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run == null)
            return Fail<PayRunDto>("Pay run not found.", 404);
        if (run.Status is not (PayRunStatus.Posted or PayRunStatus.PartiallyPaid or PayRunStatus.Paid))
            return Fail<PayRunDto>("Only a posted (or paid) pay run can be voided.", 409);

        var paymentEntries = await _ledgerPosting.GetBySourceAsync(
            PayrollPostingHelper.PayRunPaymentSourceType, run.Id.ToString(), cancellationToken);
        var postingEntries = await _ledgerPosting.GetBySourceAsync(
            PayrollPostingHelper.PayRunSourceType, run.Id.ToString(), cancellationToken);

        // 付款先冲销、过账后冲销；只冲销尚未冲销的原始凭证（排除冲销凭证本身）
        var toReverse = paymentEntries.Data!
            .Concat(postingEntries.Data!)
            .Where(e => e.Status == JournalEntryStatus.Posted && e.ReversalOfEntryId == null)
            .ToList();

        var reversalIds = new List<Guid>();
        try
        {
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                foreach (var entry in toReverse)
                {
                    var reversed = await _ledgerPosting.ReverseAsync(entry.Id, null, ct);
                    if (!reversed.Succeeded)
                        throw new PayrollUnitOfWorkAbortException(Result.Failure(reversed.Message ?? "Reversal failed.", reversed.Code ?? 400));
                    reversalIds.Add(reversed.Data!.Id);
                }

                run.Status = PayRunStatus.Voided;
                await _runRepo.UpdateAsync(run, ct);
                await _runRepo.SaveChangesAsync(ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (PayrollUnitOfWorkAbortException ex)
        {
            return Fail<PayRunDto>(ex.Result.Message ?? "Void failed.", ex.Result.Code ?? 400);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<PayRunDto>("The pay run was modified concurrently. Reload and try again.", 409);
        }

        await PublishEventAsync(new FinanceDocumentVoidedEvent
        {
            DocType = PayrollPostingHelper.PayRunSourceType,
            DocId = run.Id,
            Number = run.Number,
            // 冲销凭证 id（对齐 Finance 各单据服务 = reversal.Id，非被冲销原凭证）
            VoidJournalEntryId = reversalIds.Count > 0 ? reversalIds[0] : Guid.Empty
        }, cancellationToken);

        return Ok(await ToDtoAsync(run, cancellationToken));
    }

    // ── Payslip 子资源 ────────────────────────────────────────────────────────

    public async Task<Result<List<PayslipListDto>>> GetPayslipsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await _runRepo.AnyAsync(r => r.Id == id, cancellationToken))
            return Fail<List<PayslipListDto>>("Pay run not found.", 404);

        var payslips = await _payslipRepo.AsNoTracking()
            .Where(p => p.PayRunId == id)
            .OrderBy(p => p.EmployeeCode)
            .ProjectTo<Payslip, PayslipListDto>()
            .ToListAsync(cancellationToken);

        return Ok(payslips);
    }

    public async Task<Result<PayslipDto>> GetPayslipAsync(Guid id, Guid payslipId, CancellationToken cancellationToken = default)
    {
        var payslip = await _payslipRepo.AsNoTracking().Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == payslipId && p.PayRunId == id, cancellationToken);
        if (payslip == null)
            return Fail<PayslipDto>("Payslip not found.", 404);

        return Ok(ToPayslipDto(payslip));
    }

    public async Task<Result<PayslipDto>> UpdatePayslipInputsAsync(Guid id, Guid payslipId, UpdatePayslipInputsDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        if (input.WorkedDays < 0)
            return Fail<PayslipDto>("WorkedDays cannot be negative.", 400);

        var run = await _runRepo.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run == null)
            return Fail<PayslipDto>("Pay run not found.", 404);
        if (run.Source != PayRunSource.Internal)
            return Fail<PayslipDto>("Only internal pay run payslips are recalculated from inputs.", 409);
        if (run.Status != PayRunStatus.Calculated)
            return Fail<PayslipDto>("Payslip inputs can only be changed while the pay run is in the Calculated state.", 409);

        var payslip = await _payslipRepo.AsQueryable(true).Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == payslipId && p.PayRunId == id, cancellationToken);
        if (payslip == null)
            return Fail<PayslipDto>("Payslip not found.", 404);

        var overrides = new Dictionary<Guid, decimal> { [payslip.EmployeeId] = input.WorkedDays };
        var calcResult = await _calculator.CalculateAsync(run, overrides, new[] { payslip.EmployeeId }, cancellationToken);
        if (!calcResult.Succeeded)
            return Fail<PayslipDto>(calcResult.Message ?? "Recalculation failed.", calcResult.Code ?? 400);
        if (calcResult.Data!.Count == 0)
            return Fail<PayslipDto>("The employee is no longer eligible for this pay run.", 400);

        var recomputed = calcResult.Data[0];

        try
        {
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var oldLines = payslip.Lines.ToList();
                if (oldLines.Count > 0)
                    await _lineRepo.DeleteManyAsync(oldLines, ct);
                payslip.Lines.Clear();

                payslip.WorkedDays = input.WorkedDays;
                payslip.BaseAmount = recomputed.BaseAmount;
                payslip.PeriodDays = recomputed.PeriodDays;
                payslip.StructureId = recomputed.StructureId;
                payslip.GrossPay = recomputed.GrossPay;
                payslip.TotalDeductions = recomputed.TotalDeductions;
                payslip.EmployerCost = recomputed.EmployerCost;
                payslip.NetPay = recomputed.NetPay;
                payslip.CalculationError = recomputed.CalculationError;

                foreach (var line in recomputed.Lines)
                {
                    line.PayslipId = payslip.Id;
                    payslip.Lines.Add(line);
                }

                await _payslipRepo.UpdateAsync(payslip, ct);
                await _payslipRepo.SaveChangesAsync(ct);

                // 重算批次聚合快照
                var all = await _payslipRepo.AsNoTracking().Where(p => p.PayRunId == id).ToListAsync(ct);
                ApplyAggregates(run, all);
                await _runRepo.UpdateAsync(run, ct);
                await _runRepo.SaveChangesAsync(ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<PayslipDto>("The pay run was modified concurrently. Reload and try again.", 409);
        }

        var reloaded = await _payslipRepo.AsNoTracking().Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == payslipId, cancellationToken);
        return Ok(ToPayslipDto(reloaded!));
    }

    // ── 内部辅助 ───────────────────────────────────────────────────────────────
    // 外部摄取（CreateFromExternalAsync + 自愈过账 + 构建）见 PayRunService.External.cs

    private static void ApplyAggregates(PayRun run, IReadOnlyList<Payslip> payslips)
    {
        run.EmployeeCount = payslips.Count;
        run.GrossTotal = payslips.Sum(p => p.GrossPay);
        run.DeductionTotal = payslips.Sum(p => p.TotalDeductions);
        run.EmployerCostTotal = payslips.Sum(p => p.EmployerCost);
        run.NetTotal = payslips.Sum(p => p.NetPay);
    }

    private async Task DeletePayslipsAsync(List<Payslip> payslips, CancellationToken cancellationToken)
    {
        if (payslips.Count == 0)
            return;
        var lines = payslips.SelectMany(p => p.Lines).ToList();
        if (lines.Count > 0)
            await _lineRepo.DeleteManyAsync(lines, cancellationToken);
        await _payslipRepo.DeleteManyAsync(payslips, cancellationToken);
    }

    private async Task<Result> ValidateDraftAsync(CreatePayRunDto input, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(input.Frequency))
            return Fail("Frequency must be Monthly, SemiMonthly, BiWeekly or Weekly.");
        if (input.PeriodStart.ToUtcDate() > input.PeriodEnd.ToUtcDate())
            return Fail("PeriodStart cannot be later than PeriodEnd.");

        if (input.StructureId.HasValue &&
            !await _structureRepo.AnyAsync(s => s.Id == input.StructureId.Value, cancellationToken))
        {
            return Fail("The selected salary structure was not found.", 404);
        }

        return Ok();
    }

    private async Task<PayRunDto> ToDtoAsync(PayRun run, CancellationToken cancellationToken)
    {
        var dto = run.MapTo<PayRunDto>();
        dto.ErrorCount = await _payslipRepo.CountAsync(p => p.PayRunId == run.Id && p.CalculationError != null, cancellationToken);

        if (run.StructureId.HasValue)
        {
            var structure = await _structureRepo.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == run.StructureId.Value, cancellationToken);
            dto.StructureName = structure?.Name;
        }

        return dto;
    }

    private static PayslipDto ToPayslipDto(Payslip payslip)
    {
        var dto = payslip.MapTo<PayslipDto>();
        dto.Lines = payslip.Lines
            .OrderBy(l => l.Sequence)
            .Select(l => l.MapTo<PayslipLineDto>())
            .ToList();
        return dto;
    }
}
