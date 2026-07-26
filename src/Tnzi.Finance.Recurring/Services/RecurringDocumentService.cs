namespace Tnzi.Finance.Recurring.Services;

/// <summary>
/// 周期性单据模板管理
/// </summary>
/// <remarks>
/// 模板本身**不是单据**：没有编号、不投影总账、金额只是按现价的估算。因此这里
/// 没有过账/作废那一套，只有排期的启停与内容的增删改。
/// </remarks>
public class RecurringDocumentService : ApplicationService, IRecurringDocumentService
{
    private readonly IRepository<RecurringDocument, Guid> _repository;
    private readonly IRepository<RecurringLine, Guid> _lineRepository;
    private readonly IReadOnlyRepository<RecurringRun, Guid> _runRepository;
    private readonly IReadOnlyRepository<Customer, Guid> _customerRepository;
    private readonly IReadOnlyRepository<Vendor, Guid> _vendorRepository;
    private readonly IRecurrenceSchedule _schedule;
    private readonly RecurringOptions _options;

    public RecurringDocumentService(
        IServiceProvider serviceProvider,
        IRepository<RecurringDocument, Guid> repository,
        IRepository<RecurringLine, Guid> lineRepository,
        IReadOnlyRepository<RecurringRun, Guid> runRepository,
        IReadOnlyRepository<Customer, Guid> customerRepository,
        IReadOnlyRepository<Vendor, Guid> vendorRepository,
        IRecurrenceSchedule schedule,
        IOptionsSnapshot<RecurringOptions> options)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _lineRepository = Check.NotNull(lineRepository);
        _runRepository = Check.NotNull(runRepository);
        _customerRepository = Check.NotNull(customerRepository);
        _vendorRepository = Check.NotNull(vendorRepository);
        _schedule = Check.NotNull(schedule);
        _options = Check.NotNull(options).Value;
    }

    public async Task<Result<IPagedList<RecurringDocumentDto>>> GetPagedAsync(RecurringDocumentQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var paged = await _repository.AsNoTracking()
            .Filter(query)
            // 该跑的排最前：这张列表的用途就是"接下来会发生什么"。
            .OrderBy(e => e.Status).ThenBy(e => e.NextRunDate)
            .Select(e => new RecurringDocumentDto
            {
                Id = e.Id,
                Name = e.Name,
                Kind = e.Kind,
                Status = e.Status,
                PartyId = e.PartyId,
                PaidFromAccountId = e.PaidFromAccountId,
                Currency = e.Currency,
                PaymentMethod = e.PaymentMethod,
                Memo = e.Memo,
                Frequency = e.Frequency,
                Interval = e.Interval,
                AnchorDay = e.AnchorDay,
                StartDate = e.StartDate,
                EndDate = e.EndDate,
                MaxOccurrences = e.MaxOccurrences,
                DueDays = e.DueDays,
                AutoPost = e.AutoPost,
                NextRunDate = e.NextRunDate,
                LastRunDate = e.LastRunDate,
                OccurrenceCount = e.OccurrenceCount,
                EstimatedTotal = e.Lines.Sum(l => l.Quantity * l.UnitPrice),
                ConcurrencyStamp = e.ConcurrencyStamp,
            })
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        foreach (var dto in paged.Items)
            dto.EffectiveAutoPost = dto.AutoPost ?? _options.DefaultAutoPost;

        await FillPartyNamesAsync([.. paged.Items], cancellationToken);
        return Ok(paged);
    }

    public async Task<Result<RecurringDocumentDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.AsNoTracking()
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entity == null)
            return Fail<RecurringDocumentDto>("Recurring template not found.", 404);

        var dto = ToDto(entity);
        await FillPartyNamesAsync([dto], cancellationToken);
        return Ok(dto);
    }

    public async Task<Result<RecurringDocumentDto>> CreateAsync(CreateRecurringDocumentDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var validation = ValidateSchedule(input.Frequency, input.Interval, input.AnchorDay, input.StartDate, input.EndDate, input.MaxOccurrences);
        if (validation != null)
            return Fail<RecurringDocumentDto>(validation, 400);

        var partyCheck = await ValidatePartyAsync(input.Kind, input.PartyId, input.PaidFromAccountId, cancellationToken);
        if (partyCheck != null)
            return Fail<RecurringDocumentDto>(partyCheck.Message!, partyCheck.Code ?? 400);

        if (input.Lines == null || input.Lines.Count == 0)
            return Fail<RecurringDocumentDto>("A recurring template needs at least one line.", 400);

        var entity = new RecurringDocument
        {
            Name = input.Name?.Trim() ?? string.Empty,
            Kind = input.Kind,
            Status = RecurringStatus.Active,
            PartyId = input.PartyId,
            PaidFromAccountId = input.PaidFromAccountId,
            Currency = string.IsNullOrWhiteSpace(input.Currency) ? null : input.Currency.Trim().ToUpperInvariant(),
            PaymentMethod = string.IsNullOrWhiteSpace(input.PaymentMethod) ? null : input.PaymentMethod.Trim(),
            Memo = input.Memo,
            Frequency = input.Frequency,
            Interval = Math.Max(1, input.Interval),
            AnchorDay = input.AnchorDay,
            StartDate = input.StartDate.ToUtcDate(),
            EndDate = input.EndDate?.ToUtcDate(),
            MaxOccurrences = input.MaxOccurrences,
            DueDays = input.DueDays,
            AutoPost = input.AutoPost,
        };
        entity.NextRunDate = _schedule.First(entity.StartDate, entity.Frequency, entity.AnchorDay);

        if (string.IsNullOrWhiteSpace(entity.Name))
            return Fail<RecurringDocumentDto>("Name is required.", 400);

        await _repository.InsertAsync(entity, cancellationToken);
        // ★写完先 flush 再回读：全局 UoW 下仓储调用不会立刻提交，而 GetAsync 走
        // AsNoTracking() 直查数据库 —— 不 flush 就会拿到"刚建出来的东西不存在"。
        await _repository.SaveChangesAsync(cancellationToken);
        await ReplaceLinesAsync(entity.Id, input.Lines, cancellationToken);

        return await GetAsync(entity.Id, cancellationToken);
    }

    public async Task<Result<RecurringDocumentDto>> UpdateAsync(Guid id, UpdateRecurringDocumentDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var entity = await _repository.GetAsync(id, cancellationToken);
        if (entity == null)
            return Fail<RecurringDocumentDto>("Recurring template not found.", 404);
        if (entity.Status == RecurringStatus.Ended)
            return Fail<RecurringDocumentDto>("This template has ended and can no longer be edited.", 409);

        var validation = ValidateSchedule(input.Frequency, input.Interval, input.AnchorDay, input.StartDate, input.EndDate, input.MaxOccurrences);
        if (validation != null)
            return Fail<RecurringDocumentDto>(validation, 400);

        var partyCheck = await ValidatePartyAsync(entity.Kind, input.PartyId, input.PaidFromAccountId, cancellationToken);
        if (partyCheck != null)
            return Fail<RecurringDocumentDto>(partyCheck.Message!, partyCheck.Code ?? 400);

        if (input.Lines == null || input.Lines.Count == 0)
            return Fail<RecurringDocumentDto>("A recurring template needs at least one line.", 400);

        var scheduleChanged = entity.Frequency != input.Frequency
            || entity.Interval != Math.Max(1, input.Interval)
            || entity.AnchorDay != input.AnchorDay
            || entity.StartDate != input.StartDate.ToUtcDate();

        entity.Name = input.Name?.Trim() ?? entity.Name;
        entity.PartyId = input.PartyId;
        entity.PaidFromAccountId = input.PaidFromAccountId;
        entity.Currency = string.IsNullOrWhiteSpace(input.Currency) ? null : input.Currency.Trim().ToUpperInvariant();
        entity.PaymentMethod = string.IsNullOrWhiteSpace(input.PaymentMethod) ? null : input.PaymentMethod.Trim();
        entity.Memo = input.Memo;
        entity.Frequency = input.Frequency;
        entity.Interval = Math.Max(1, input.Interval);
        entity.AnchorDay = input.AnchorDay;
        entity.StartDate = input.StartDate.ToUtcDate();
        entity.EndDate = input.EndDate?.ToUtcDate();
        entity.MaxOccurrences = input.MaxOccurrences;
        entity.DueDays = input.DueDays;
        entity.AutoPost = input.AutoPost;
        entity.ConcurrencyStamp = input.ConcurrencyStamp;

        // 改了排期规则就重算下一次；只改内容（价格/摘要）不动排期 —— 涨个价不该
        // 让下一期悄悄挪到别的日子。
        if (scheduleChanged)
        {
            entity.NextRunDate = entity.LastRunDate.HasValue
                ? _schedule.Next(entity.LastRunDate.Value, entity.Frequency, entity.Interval, entity.AnchorDay)
                : _schedule.First(entity.StartDate, entity.Frequency, entity.AnchorDay);
        }

        try
        {
            await _repository.UpdateAsync(entity, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<RecurringDocumentDto>("This template was changed by someone else. Reload and try again.", 409);
        }

        await ReplaceLinesAsync(entity.Id, input.Lines, cancellationToken);
        return await GetAsync(entity.Id, cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetAsync(id, cancellationToken);
        if (entity == null)
            return Fail("Recurring template not found.", 404);

        var hasRuns = await _runRepository.AnyAsync(r => r.RecurringDocumentId == id, cancellationToken);
        if (hasRuns)
        {
            return Fail(
                "This template has already generated documents; end it instead of deleting so their origin stays traceable.",
                409);
        }

        await _repository.DeleteAsync(entity, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    public Task<Result<RecurringDocumentDto>> PauseAsync(Guid id, CancellationToken cancellationToken = default)
        => TransitionAsync(id, RecurringStatus.Paused, cancellationToken);

    public Task<Result<RecurringDocumentDto>> ResumeAsync(Guid id, CancellationToken cancellationToken = default)
        => TransitionAsync(id, RecurringStatus.Active, cancellationToken);

    public Task<Result<RecurringDocumentDto>> EndAsync(Guid id, CancellationToken cancellationToken = default)
        => TransitionAsync(id, RecurringStatus.Ended, cancellationToken);

    public async Task<Result<RecurrencePreviewDto>> PreviewAsync(Guid id, int count = 6, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entity == null)
            return Fail<RecurrencePreviewDto>("Recurring template not found.", 404);

        return Ok(new RecurrencePreviewDto
        {
            Dates = Project(entity.NextRunDate, entity.Frequency, entity.Interval, entity.AnchorDay,
                entity.EndDate, RemainingOccurrences(entity), count),
        });
    }

    public Result<RecurrencePreviewDto> PreviewSchedule(CreateRecurringDocumentDto input, int count = 6)
    {
        Check.NotNull(input);

        var validation = ValidateSchedule(input.Frequency, input.Interval, input.AnchorDay, input.StartDate, input.EndDate, input.MaxOccurrences);
        if (validation != null)
            return Fail<RecurrencePreviewDto>(validation, 400);

        var first = _schedule.First(input.StartDate.ToUtcDate(), input.Frequency, input.AnchorDay);
        return Ok(new RecurrencePreviewDto
        {
            Dates = Project(first, input.Frequency, Math.Max(1, input.Interval), input.AnchorDay,
                input.EndDate?.ToUtcDate(), input.MaxOccurrences, count),
        });
    }

    public async Task<Result<IPagedList<RecurringRunDto>>> GetRunsAsync(RecurringRunQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var paged = await _runRepository.AsNoTracking()
            .Filter(query)
            .OrderByDescending(e => e.PeriodDate).ThenByDescending(e => e.CreationTime)
            .Select(e => new RecurringRunDto
            {
                Id = e.Id,
                RecurringDocumentId = e.RecurringDocumentId,
                PeriodDate = e.PeriodDate,
                Status = e.Status,
                DocType = e.DocType,
                DocId = e.DocId,
                DocNumber = e.DocNumber,
                Posted = e.Posted,
                FailReason = e.FailReason,
                CreationTime = e.CreationTime,
            })
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        // 模板名单独批量解析：生成记录页里"这是哪条模板跑出来的"是第一眼要看的。
        var ids = paged.Items.Select(r => r.RecurringDocumentId).Distinct().ToList();
        if (ids.Count > 0)
        {
            var names = await _repository.AsNoTracking()
                .Where(e => ids.Contains(e.Id))
                .Select(e => new { e.Id, e.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
            foreach (var run in paged.Items)
                run.RecurringDocumentName = names.GetValueOrDefault(run.RecurringDocumentId);
        }

        return Ok(paged);
    }

    // ── 内部 ──────────────────────────────────────────────

    private async Task<Result<RecurringDocumentDto>> TransitionAsync(Guid id, RecurringStatus target, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetAsync(id, cancellationToken);
        if (entity == null)
            return Fail<RecurringDocumentDto>("Recurring template not found.", 404);

        if (entity.Status == RecurringStatus.Ended)
            return Fail<RecurringDocumentDto>("This template has ended; create a new one instead.", 409);
        if (entity.Status == target)
            return await GetAsync(id, cancellationToken);

        // 恢复时把排期推到今天之后：暂停期间的期次是被人为决定不要的，
        // 续上等于恢复的瞬间凭空补出一批单据。
        if (target == RecurringStatus.Active && entity.Status == RecurringStatus.Paused)
        {
            var today = DateTime.UtcNow.ToUtcDate();
            var next = entity.NextRunDate;
            var guard = 0;
            while (next < today && guard++ < MaxScheduleProjection)
                next = _schedule.Next(next, entity.Frequency, entity.Interval, entity.AnchorDay);
            entity.NextRunDate = next;
        }

        entity.Status = target;
        await _repository.UpdateAsync(entity, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task ReplaceLinesAsync(Guid documentId, List<CreateRecurringLineDto> lines, CancellationToken cancellationToken)
    {
        // 行硬删重建：与全模块单据行一致。模板行没有独立身份，改一行与换一行
        // 在业务上是同一件事。
        var existing = await _lineRepository.Where(l => l.RecurringDocumentId == documentId).ToListAsync(cancellationToken);
        if (existing.Count > 0)
            await _lineRepository.DeleteManyAsync(existing, cancellationToken);

        var index = 1;
        var fresh = lines.Select(l => new RecurringLine
        {
            RecurringDocumentId = documentId,
            LineNumber = index++,
            ItemId = l.ItemId,
            Description = l.Description,
            AccountId = l.AccountId,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            TaxCodeId = l.TaxCodeId,
        }).ToList();

        await _lineRepository.InsertManyAsync(fresh, cancellationToken);
        await _lineRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task<Result?> ValidatePartyAsync(RecurringDocKind kind, Guid partyId, Guid? paidFromAccountId, CancellationToken cancellationToken)
    {
        if (partyId == Guid.Empty)
            return Fail(kind == RecurringDocKind.Invoice ? "Customer is required." : "Vendor is required.", 400);

        var exists = kind == RecurringDocKind.Invoice
            ? await _customerRepository.AnyAsync(c => c.Id == partyId, cancellationToken)
            : await _vendorRepository.AnyAsync(v => v.Id == partyId, cancellationToken);
        if (!exists)
            return Fail(kind == RecurringDocKind.Invoice ? "Customer not found." : "Vendor not found.", 404);

        if (kind == RecurringDocKind.Expense && paidFromAccountId is null)
            return Fail("An expense template needs the account it is paid from.", 400);

        return null;
    }

    private static string? ValidateSchedule(
        RecurrenceFrequency frequency, int interval, int? anchorDay, DateTime startDate, DateTime? endDate, int? maxOccurrences)
    {
        if (interval < 1)
            return "Interval must be at least 1.";
        if (startDate == default)
            return "Start date is required.";
        if (endDate.HasValue && endDate.Value.ToUtcDate() < startDate.ToUtcDate())
            return "The end date falls before the start date.";
        if (maxOccurrences is <= 0)
            return "Maximum occurrences must be greater than zero.";

        if (anchorDay.HasValue)
        {
            var max = frequency == RecurrenceFrequency.Weekly ? 7 : 31;
            if (anchorDay.Value < 1 || anchorDay.Value > max)
            {
                return frequency == RecurrenceFrequency.Weekly
                    ? "Anchor day must be between 1 (Monday) and 7 (Sunday)."
                    : "Anchor day must be between 1 and 31.";
            }
        }

        return null;
    }

    private static int? RemainingOccurrences(RecurringDocument entity)
        => entity.MaxOccurrences.HasValue ? Math.Max(0, entity.MaxOccurrences.Value - entity.OccurrenceCount) : null;

    private List<DateTime> Project(
        DateTime first, RecurrenceFrequency frequency, int interval, int? anchorDay,
        DateTime? endDate, int? remaining, int count)
    {
        var take = Math.Clamp(count, 1, MaxScheduleProjection);
        var dates = new List<DateTime>();
        var cursor = first;

        while (dates.Count < take)
        {
            if (endDate.HasValue && cursor > endDate.Value)
                break;
            if (remaining.HasValue && dates.Count >= remaining.Value)
                break;

            dates.Add(cursor);
            cursor = _schedule.Next(cursor, frequency, interval, anchorDay);
        }

        return dates;
    }

    private async Task FillPartyNamesAsync(IReadOnlyList<RecurringDocumentDto> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;

        var customerIds = items.Where(i => i.Kind == RecurringDocKind.Invoice).Select(i => i.PartyId).Distinct().ToList();
        var vendorIds = items.Where(i => i.Kind != RecurringDocKind.Invoice).Select(i => i.PartyId).Distinct().ToList();

        var customers = customerIds.Count == 0
            ? []
            : await _customerRepository.AsNoTracking().Where(c => customerIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var vendors = vendorIds.Count == 0
            ? []
            : await _vendorRepository.AsNoTracking().Where(v => vendorIds.Contains(v.Id))
                .Select(v => new { v.Id, v.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        foreach (var item in items)
        {
            item.PartyName = item.Kind == RecurringDocKind.Invoice
                ? customers.GetValueOrDefault(item.PartyId)
                : vendors.GetValueOrDefault(item.PartyId);
        }
    }

    private RecurringDocumentDto ToDto(RecurringDocument e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Kind = e.Kind,
        Status = e.Status,
        PartyId = e.PartyId,
        PaidFromAccountId = e.PaidFromAccountId,
        Currency = e.Currency,
        PaymentMethod = e.PaymentMethod,
        Memo = e.Memo,
        Frequency = e.Frequency,
        Interval = e.Interval,
        AnchorDay = e.AnchorDay,
        StartDate = e.StartDate,
        EndDate = e.EndDate,
        MaxOccurrences = e.MaxOccurrences,
        DueDays = e.DueDays,
        AutoPost = e.AutoPost,
        EffectiveAutoPost = e.AutoPost ?? _options.DefaultAutoPost,
        NextRunDate = e.NextRunDate,
        LastRunDate = e.LastRunDate,
        OccurrenceCount = e.OccurrenceCount,
        EstimatedTotal = e.Lines.Sum(l => l.Quantity * l.UnitPrice),
        ConcurrencyStamp = e.ConcurrencyStamp,
        Lines = [.. e.Lines.OrderBy(l => l.LineNumber).Select(l => new RecurringLineDto
        {
            Id = l.Id,
            LineNumber = l.LineNumber,
            ItemId = l.ItemId,
            Description = l.Description,
            AccountId = l.AccountId,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            TaxCodeId = l.TaxCodeId,
            Amount = l.Quantity * l.UnitPrice,
        })],
    };

    /// <summary>
    /// 排期推演的硬上限。
    /// </summary>
    /// <remarks>
    /// 护栏而非设计：<see cref="IRecurrenceSchedule"/> 的实现被要求严格递增，但
    /// 那是消费方可替换的代码 —— 一个写错的实现不该让整个进程转不出来。
    /// </remarks>
    private const int MaxScheduleProjection = 120;
}
