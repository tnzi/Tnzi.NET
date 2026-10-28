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
    private readonly IReadOnlyRepository<Item, Guid> _itemRepository;
    private readonly IReadOnlyRepository<Account, Guid> _accountRepository;
    private readonly IReadOnlyRepository<TaxCode, Guid> _taxCodeRepository;
    private readonly IRecurrenceSchedule _schedule;
    private readonly RecurringOptions _options;

    public RecurringDocumentService(
        IServiceProvider serviceProvider,
        IRepository<RecurringDocument, Guid> repository,
        IRepository<RecurringLine, Guid> lineRepository,
        IReadOnlyRepository<RecurringRun, Guid> runRepository,
        IReadOnlyRepository<Customer, Guid> customerRepository,
        IReadOnlyRepository<Vendor, Guid> vendorRepository,
        IReadOnlyRepository<Item, Guid> itemRepository,
        IReadOnlyRepository<Account, Guid> accountRepository,
        IReadOnlyRepository<TaxCode, Guid> taxCodeRepository,
        IRecurrenceSchedule schedule,
        IOptionsSnapshot<RecurringOptions> options)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _lineRepository = Check.NotNull(lineRepository);
        _runRepository = Check.NotNull(runRepository);
        _customerRepository = Check.NotNull(customerRepository);
        _vendorRepository = Check.NotNull(vendorRepository);
        _itemRepository = Check.NotNull(itemRepository);
        _accountRepository = Check.NotNull(accountRepository);
        _taxCodeRepository = Check.NotNull(taxCodeRepository);
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

        // 未定义的种类会被当成「供应商侧」单据一路走下去，到生成时才以「不支持的种类」失败。
        if (!Enum.IsDefined(input.Kind))
            return Fail<RecurringDocumentDto>("Kind must be Invoice, Bill or Expense.", 400);

        var validation = ValidateSchedule(input.Frequency, input.Interval, input.AnchorDay, input.StartDate, input.EndDate, input.MaxOccurrences);
        if (validation != null)
            return Fail<RecurringDocumentDto>(validation, 400);

        var partyCheck = await ValidatePartyAsync(input.Kind, input.PartyId, input.PaidFromAccountId, cancellationToken);
        if (partyCheck != null)
            return Fail<RecurringDocumentDto>(partyCheck.Message!, partyCheck.Code ?? 400);

        if (input.Lines == null || input.Lines.Count == 0)
            return Fail<RecurringDocumentDto>("A recurring template needs at least one line.", 400);

        var contentCheck = await ValidateContentAsync(
            input.Kind, input.Name, input.Currency, input.PaymentMethod, input.Memo, input.DueDays, input.Lines, cancellationToken);
        if (contentCheck != null)
            return Fail<RecurringDocumentDto>(contentCheck, 400);

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
        entity.NextRunDate = _schedule.First(entity.StartDate, entity.Frequency, RecurrenceAnchor.Effective(entity));

        if (string.IsNullOrWhiteSpace(entity.Name))
            return Fail<RecurringDocumentDto>("Name is required.", 400);

        // ★ 头与行经导航属性一次插入（同一次 SaveChanges、同一个事务）：先提交头再写行的话，
        // 行写入失败会留下一条 Active 而零行的模板，此后每一期都以「没有行」失败。
        // 行的外键由 EF 沿导航属性在提交时回填。
        foreach (var line in BuildLines(entity.Id, input.Lines))
            entity.Lines.Add(line);

        await _repository.InsertAsync(entity, cancellationToken);
        // ★写完先 flush 再回读：全局 UoW 下仓储调用不会立刻提交，而 GetAsync 走
        // AsNoTracking() 直查数据库 —— 不 flush 就会拿到"刚建出来的东西不存在"。
        await _repository.SaveChangesAsync(cancellationToken);

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

        // ★ 客户端的 stamp 要显式比对，不能赋给已跟踪实体：EF 的并发谓词用的是加载时的 OriginalValue，
        // 赋值只改 CurrentValue，随后审计助手又把它覆写成新 Guid —— 那一行既不参与判定也不落库，
        // 两个管理员同时编辑，后提交的静默覆盖先提交的，而模板决定的是「未来每一期开多少钱」。
        // 留空视为「客户端不比对」，兼容从没回传过它的消费方；下面的 DbUpdateConcurrencyException
        // 仍守着同一请求内 load→save 的窗口。
        if (!string.IsNullOrEmpty(input.ConcurrencyStamp)
            && !string.Equals(entity.ConcurrencyStamp, input.ConcurrencyStamp, StringComparison.Ordinal))
        {
            return Fail<RecurringDocumentDto>("This template was changed by someone else. Reload and try again.", 409);
        }

        var validation = ValidateSchedule(input.Frequency, input.Interval, input.AnchorDay, input.StartDate, input.EndDate, input.MaxOccurrences);
        if (validation != null)
            return Fail<RecurringDocumentDto>(validation, 400);

        var partyCheck = await ValidatePartyAsync(entity.Kind, input.PartyId, input.PaidFromAccountId, cancellationToken);
        if (partyCheck != null)
            return Fail<RecurringDocumentDto>(partyCheck.Message!, partyCheck.Code ?? 400);

        if (input.Lines == null || input.Lines.Count == 0)
            return Fail<RecurringDocumentDto>("A recurring template needs at least one line.", 400);

        var contentCheck = await ValidateContentAsync(
            entity.Kind, input.Name, input.Currency, input.PaymentMethod, input.Memo, input.DueDays, input.Lines, cancellationToken);
        if (contentCheck != null)
            return Fail<RecurringDocumentDto>(contentCheck, 400);

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

        // 改了排期规则就重算下一次；只改内容（价格/摘要）不动排期 —— 涨个价不该
        // 让下一期悄悄挪到别的日子。
        if (scheduleChanged)
        {
            entity.NextRunDate = entity.LastRunDate.HasValue
                ? _schedule.Next(entity.LastRunDate.Value, entity.Frequency, entity.Interval, RecurrenceAnchor.Effective(entity))
                : _schedule.First(entity.StartDate, entity.Frequency, RecurrenceAnchor.Effective(entity));
        }

        // ★ 头与行在同一个工作单元里：行是硬删重建，分开提交时新行写入失败，
        // 旧行已经删掉并提交，模板 Active 而零行，原来的价格也没了。
        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                await _repository.UpdateAsync(entity, ct);
                await _repository.SaveChangesAsync(ct);
                await ReplaceLinesAsync(entity.Id, input.Lines, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<RecurringDocumentDto>("This template was changed by someone else. Reload and try again.", 409);
        }

        return await GetAsync(entity.Id, cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetAsync(id, cancellationToken);
        if (entity == null)
            return Fail("Recurring template not found.", 404);

        // 只数真的生成过单据的期次：失败的那一期随本期事务回滚、跳过的那一期本就没有单据，
        // 它们不构成「来历无从查起」的理由。
        var hasGenerated = await _runRepository.AnyAsync(
            r => r.RecurringDocumentId == id && r.Status == RecurringRunStatus.Generated, cancellationToken);
        if (hasGenerated)
        {
            return Fail(
                "This template has already generated documents; end it instead of deleting so their origin stays traceable.",
                409);
        }

        try
        {
            await _repository.DeleteAsync(entity, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 软删是一次带并发戳的 UPDATE：读完「还没生成过单据」之后，扫描可能已经推进了它。
            return Fail("This template was changed by someone else. Reload and try again.", 409);
        }
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
            Dates = Project(entity.NextRunDate, entity.Frequency, entity.Interval, RecurrenceAnchor.Effective(entity),
                entity.EndDate, RemainingOccurrences(entity), count),
        });
    }

    public Result<RecurrencePreviewDto> PreviewSchedule(CreateRecurringDocumentDto input, int count = 6)
    {
        Check.NotNull(input);

        var validation = ValidateSchedule(input.Frequency, input.Interval, input.AnchorDay, input.StartDate, input.EndDate, input.MaxOccurrences);
        if (validation != null)
            return Fail<RecurrencePreviewDto>(validation, 400);

        var startDate = input.StartDate.ToUtcDate();
        var anchorDay = RecurrenceAnchor.Effective(input.Frequency, input.AnchorDay, startDate);
        var first = _schedule.First(startDate, input.Frequency, anchorDay);
        return Ok(new RecurrencePreviewDto
        {
            Dates = Project(first, input.Frequency, Math.Max(1, input.Interval), anchorDay,
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
            var today = TimeProvider.GetUtcNow().UtcDateTime.ToUtcDate();
            var next = entity.NextRunDate;
            var guard = 0;
            while (next < today && guard++ < MaxScheduleProjection)
                next = _schedule.Next(next, entity.Frequency, entity.Interval, RecurrenceAnchor.Effective(entity));
            entity.NextRunDate = next;
        }

        entity.Status = target;
        try
        {
            await _repository.UpdateAsync(entity, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 后台扫描的 AdvanceAsync 或另一个管理员的编辑可能在读与写之间改了同一个模板；
            // 并发戳挡住这次过期的写，但那是 409 不是 500（同 UpdateAsync）。
            return Fail<RecurringDocumentDto>("This template was changed by someone else. Reload and try again.", 409);
        }
        return await GetAsync(id, cancellationToken);
    }

    private async Task ReplaceLinesAsync(Guid documentId, List<CreateRecurringLineDto> lines, CancellationToken cancellationToken)
    {
        // 行硬删重建：与全模块单据行一致。模板行没有独立身份，改一行与换一行
        // 在业务上是同一件事。
        var existing = await _lineRepository.Where(l => l.RecurringDocumentId == documentId).ToListAsync(cancellationToken);
        if (existing.Count > 0)
            await _lineRepository.DeleteManyAsync(existing, cancellationToken);

        await _lineRepository.InsertManyAsync(BuildLines(documentId, lines), cancellationToken);
        await _lineRepository.SaveChangesAsync(cancellationToken);
    }

    private static List<RecurringLine> BuildLines(Guid documentId, List<CreateRecurringLineDto> lines)
    {
        var index = 1;
        return [.. lines.Select(l => new RecurringLine
        {
            RecurringDocumentId = documentId,
            LineNumber = index++,
            ItemId = l.ItemId,
            Description = l.Description,
            AccountId = l.AccountId,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            TaxCodeId = l.TaxCodeId,
        })];
    }

    /// <summary>
    /// 保存时就能判定的内容错误：字段长度、到期天数、行的数量 / 单价 / 科目，以及行引用的目录项、科目、税码是否存在。
    /// </summary>
    /// <remarks>
    /// ★ 这些错此前要等到期那一天才在生成时暴露：保存返回 200、预览一切正常，到期后每一期落一行 Failed，
    /// 重试 <see cref="RecurringOptions.MaxFailedRetries"/> 次之后这一期不再尝试，单据从未开出，除非有人去翻生成记录。
    /// 规则与生成目标（发票 / 账单 / 费用单）的草稿校验同一口径；生成时的同类检查仍保留，
    /// 因为主数据在保存之后仍可能被停用。超长字段在 SQLite 上「保存成功」，在 SQL Server / PostgreSQL 上则是截断 500。
    /// </remarks>
    private async Task<string?> ValidateContentAsync(
        RecurringDocKind kind, string? name, string? currency, string? paymentMethod, string? memo, int? dueDays,
        List<CreateRecurringLineDto> lines, CancellationToken cancellationToken)
    {
        var tooLong = FirstTooLong(
            (name?.Trim(), NameMaxLength, "name"),
            (currency?.Trim(), CurrencyMaxLength, "currency"),
            (paymentMethod?.Trim(), PaymentMethodMaxLength, "payment method"),
            (memo, MemoMaxLength, "memo"));
        if (tooLong != null)
            return tooLong;
        if (dueDays is < 0)
            return "Due days cannot be negative.";

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var number = i + 1;
            if (line == null)
                return $"Line {number} is empty.";
            if (line.Description?.Length > LineDescriptionMaxLength)
                return $"Line {number}: the description must be at most {LineDescriptionMaxLength} characters.";
            if (line.Quantity <= 0)
                return $"Line {number}: quantity must be greater than zero.";
            if (line.UnitPrice < 0)
                return $"Line {number}: unit price must not be negative.";
            // 费用单按金额（数量 × 单价）落行，且要求金额为正、每行带费用科目
            if (kind == RecurringDocKind.Expense && line.UnitPrice == 0)
                return $"Line {number}: an expense line needs an amount greater than zero.";
            if (kind == RecurringDocKind.Expense && line.AccountId is null)
                return $"Line {number} has no expense account.";
        }

        var itemIds = lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId!.Value).Distinct().ToList();
        if (itemIds.Count > 0 && await _itemRepository.CountAsync(i => itemIds.Contains(i.Id), cancellationToken) != itemIds.Count)
            return "One or more catalog items on the lines do not exist.";

        var accountIds = lines.Where(l => l.AccountId.HasValue).Select(l => l.AccountId!.Value).Distinct().ToList();
        if (accountIds.Count > 0 && await _accountRepository.CountAsync(a => accountIds.Contains(a.Id), cancellationToken) != accountIds.Count)
            return "One or more accounts on the lines do not exist.";

        var taxCodeIds = lines.Where(l => l.TaxCodeId.HasValue).Select(l => l.TaxCodeId!.Value).Distinct().ToList();
        if (taxCodeIds.Count > 0 && await _taxCodeRepository.CountAsync(t => taxCodeIds.Contains(t.Id), cancellationToken) != taxCodeIds.Count)
            return "One or more tax codes on the lines do not exist.";

        return null;
    }

    private static string? FirstTooLong(params (string? Value, int MaxLength, string Field)[] fields)
    {
        foreach (var (value, maxLength, field) in fields)
        {
            if (value != null && value.Length > maxLength)
                return $"The {field} must be at most {maxLength} characters.";
        }
        return null;
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
        if (!Enum.IsDefined(frequency))
            return "Frequency must be Daily, Weekly, Monthly, Quarterly or Yearly.";
        // ★ 上界不是品味：新建只调 First()（不用间隔），所以一个离谱的间隔能存进库，到期那一刻
        // 排期越过 9999 年抛异常；再大一点，换算成月数的乘法溢出成负数，排期倒着走。
        if (interval < 1 || interval > MaxInterval)
            return $"Interval must be between 1 and {MaxInterval}.";
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

    /// <summary>排期间隔的上界（每 1000 个周期一次：换算成月数的乘法不会溢出，正常的起始日也到不了日期上限）。</summary>
    internal const int MaxInterval = 1000;

    /// <summary><see cref="RecurringDocument.Name"/> 列宽</summary>
    internal const int NameMaxLength = 200;

    /// <summary><see cref="RecurringDocument.Currency"/> 列宽</summary>
    internal const int CurrencyMaxLength = 8;

    /// <summary><see cref="RecurringDocument.PaymentMethod"/> 列宽</summary>
    internal const int PaymentMethodMaxLength = 32;

    /// <summary><see cref="RecurringDocument.Memo"/> 列宽</summary>
    internal const int MemoMaxLength = 500;

    /// <summary><see cref="RecurringLine.Description"/> 列宽</summary>
    internal const int LineDescriptionMaxLength = 500;
}
