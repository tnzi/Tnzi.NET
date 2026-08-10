namespace Tnzi.Finance.Recurring.Services;

/// <summary>
/// 到期生成
/// </summary>
/// <remarks>
/// 三条不变量，缺一条这个模块就不该上生产：
/// <list type="number">
/// <item><b>幂等</b> —— 每一期先写 <see cref="RecurringRun"/> 再造单据，唯一索引兜住
///   重跑与并发。给客户重复开一张发票是要打电话道歉的事故。</item>
/// <item><b>一期失败不拖累其它期</b> —— 每期一个独立事务；第三期的科目被停用，
///   不该让前两期一起回滚，也不该让第四期不再尝试。</item>
/// <item><b>失败留痕</b> —— 失败同样落记录（不占幂等键，下次重试）。悄悄跳过的
///   那一期，没有人会发现。</item>
/// </list>
/// </remarks>
public class RecurringGeneratorService : ApplicationService, IRecurringGeneratorService
{
    private readonly IRepository<RecurringDocument, Guid> _repository;
    private readonly IRepository<RecurringRun, Guid> _runRepository;
    private readonly RecurringDocumentBuilder _builder;
    private readonly IRecurrenceSchedule _schedule;
    private readonly RecurringOptions _options;

    public RecurringGeneratorService(
        IServiceProvider serviceProvider,
        IRepository<RecurringDocument, Guid> repository,
        IRepository<RecurringRun, Guid> runRepository,
        RecurringDocumentBuilder builder,
        IRecurrenceSchedule schedule,
        IOptionsSnapshot<RecurringOptions> options)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _runRepository = Check.NotNull(runRepository);
        _builder = Check.NotNull(builder);
        _schedule = Check.NotNull(schedule);
        _options = Check.NotNull(options).Value;
    }

    public async Task<Result<RecurringSweepResultDto>> RunDueAsync(DateTime? asOf = null, CancellationToken cancellationToken = default)
    {
        var today = (asOf ?? DateTime.UtcNow).ToUtcDate();

        var due = await _repository
            .Where(e => e.Status == RecurringStatus.Active && e.NextRunDate <= today)
            .Include(e => e.Lines)
            .ToListAsync(cancellationToken);

        // ★排期已经推过今天、但还有失败期次没补上的模板同样要进这一轮。
        // 只按 NextRunDate 取，等于失败的那一期永远不再被任何人提起。
        if (_options.MaxFailedRetries > 1)
        {
            var dueIds = due.Select(t => t.Id).ToHashSet();
            var withFailures = await _runRepository
                .Where(r => r.Status == RecurringRunStatus.Failed)
                .Select(r => r.RecurringDocumentId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var extraIds = withFailures.Where(id => !dueIds.Contains(id)).ToList();
            if (extraIds.Count > 0)
            {
                var extra = await _repository
                    .Where(e => e.Status == RecurringStatus.Active && extraIds.Contains(e.Id))
                    .Include(e => e.Lines)
                    .ToListAsync(cancellationToken);
                due.AddRange(extra);
            }
        }

        return await SweepAsync(due, today, cancellationToken);
    }

    public async Task<Result<RecurringSweepResultDto>> RunOneAsync(
        Guid recurringDocumentId, DateTime? asOf = null, CancellationToken cancellationToken = default)
    {
        var today = (asOf ?? DateTime.UtcNow).ToUtcDate();

        var template = await _repository
            .Where(e => e.Id == recurringDocumentId)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(cancellationToken);
        if (template == null)
            return Fail<RecurringSweepResultDto>("Recurring template not found.", 404);
        if (template.Status != RecurringStatus.Active)
            return Fail<RecurringSweepResultDto>("Only an active template can be run.", 409);

        // 排期在未来但有失败期次待补时，「立即运行」应当去补它 —— 那正是把科目改回来
        // 之后操作员会做的动作，而此时日历上确实没有到期的东西。
        if (template.NextRunDate > today
            && (await ResolveRetriesAsync(template, [], cancellationToken)).Count == 0)
        {
            return Fail<RecurringSweepResultDto>($"Nothing is due yet; the next run is {template.NextRunDate:yyyy-MM-dd}.", 409);
        }

        return await SweepAsync([template], today, cancellationToken);
    }

    private async Task<Result<RecurringSweepResultDto>> SweepAsync(
        List<RecurringDocument> templates, DateTime today, CancellationToken cancellationToken)
    {
        var result = new RecurringSweepResultDto { TemplatesDue = templates.Count };

        foreach (var template in templates)
        {
            var periods = ResolvePeriods(template, today, out var skipped);

            foreach (var period in skipped)
            {
                var run = await RecordSkippedAsync(template, period, cancellationToken);
                if (run != null)
                {
                    result.Skipped++;
                    result.Runs.Add(run);
                }
            }

            // 失败过的期次先补：排期无条件往前推，日历此后不会再扫到它们。
            var retries = await ResolveRetriesAsync(template, periods, cancellationToken);

            foreach (var period in retries.Concat(periods))
            {
                var run = await GeneratePeriodAsync(template, period, cancellationToken);
                if (run == null)
                    continue;

                result.Runs.Add(run);
                if (run.Status == RecurringRunStatus.Generated)
                    result.Generated++;
                else if (run.Status == RecurringRunStatus.Failed)
                    result.Failed++;
            }

            await AdvanceAsync(template, today, cancellationToken);
        }

        return Ok(result);
    }

    /// <summary>
    /// 这次该补哪几期。
    /// </summary>
    /// <remarks>
    /// ★补齐语义**由消费方配置决定**，不是框架的判断：作业停了一周，该补出七张
    /// 日租发票（GenerateAll）、只补最近一张（LatestOnly）、还是一张都不补
    /// （Skip），三种答案在不同生意里都是对的，而猜错的代价是凭空多出或少掉真金
    /// 白银的单据。
    ///
    /// 被策略排除的期次照样以 <see cref="RecurringRunStatus.Skipped"/> 留痕：跳过是
    /// 一个决定，不是什么都没发生。
    /// </remarks>
    private List<DateTime> ResolvePeriods(RecurringDocument template, DateTime today, out List<DateTime> skipped)
    {
        var all = new List<DateTime>();
        var cursor = template.NextRunDate;
        var remaining = template.MaxOccurrences.HasValue
            ? Math.Max(0, template.MaxOccurrences.Value - template.OccurrenceCount)
            : int.MaxValue;

        while (cursor <= today && all.Count < _options.MaxCatchUpPerRun && all.Count < remaining)
        {
            if (template.EndDate.HasValue && cursor > template.EndDate.Value)
                break;
            all.Add(cursor);
            cursor = _schedule.Next(cursor, template.Frequency, template.Interval, template.AnchorDay);
        }

        skipped = [];
        if (all.Count <= 1)
            return all;

        return _options.CatchUpPolicy switch
        {
            RecurringCatchUpPolicy.GenerateAll => all,
            RecurringCatchUpPolicy.LatestOnly => Split(all, keepLast: true, out skipped),
            RecurringCatchUpPolicy.Skip => Split(all, keepLast: false, out skipped),
            _ => all,
        };
    }

    /// <summary>
    /// 这次该重试哪几期。
    /// </summary>
    /// <remarks>
    /// ★没有这一步，"失败留痕可重试"就只是句话：<see cref="AdvanceAsync"/> 无条件把
    /// 排期推过今天（那是对的，否则一条坏模板会卡住自己的整条排期），于是失败的那一期
    /// 此后再也不会出现在 <see cref="ResolvePeriods"/> 的结果里 —— 科目启用回来之后
    /// 那张发票永远补不上。唯一索引之所以刻意排除 Failed 行，正是为了让这一期能被
    /// 重新插入；这里就是那个"重新提交"的人。
    ///
    /// 已经有非失败记录的期次（生成过、或被补齐策略跳过）算办完了，不再碰。
    /// 尝试次数到 <see cref="RecurringOptions.MaxFailedRetries"/> 即停：一条永远失败的
    /// 模板不该每轮都往记录表里多写一行。
    /// </remarks>
    private async Task<List<DateTime>> ResolveRetriesAsync(
        RecurringDocument template, List<DateTime> due, CancellationToken cancellationToken)
    {
        if (_options.MaxFailedRetries <= 1)
            return [];

        var attempts = await _runRepository
            .Where(r => r.RecurringDocumentId == template.Id && r.Status == RecurringRunStatus.Failed)
            .GroupBy(r => r.PeriodDate)
            .Select(g => new { Period = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        if (attempts.Count == 0)
            return [];

        // 唯一索引带 Status <> Failed 过滤，故每期至多一行"已办完"的记录。
        var settled = await _runRepository
            .Where(r => r.RecurringDocumentId == template.Id && r.Status != RecurringRunStatus.Failed)
            .Select(r => r.PeriodDate)
            .ToListAsync(cancellationToken);
        var settledSet = settled.ToHashSet();
        var dueSet = due.ToHashSet();

        return [.. attempts
            .Where(a => a.Count < _options.MaxFailedRetries
                        && !settledSet.Contains(a.Period)
                        && !dueSet.Contains(a.Period))
            .Select(a => a.Period)
            .OrderBy(d => d)
            .Take(_options.MaxCatchUpPerRun)];
    }

    private static List<DateTime> Split(List<DateTime> all, bool keepLast, out List<DateTime> skipped)
    {
        if (keepLast)
        {
            skipped = [.. all.Take(all.Count - 1)];
            return [all[^1]];
        }

        skipped = [.. all];
        return [];
    }

    /// <summary>
    /// 造出一期。
    /// </summary>
    /// <remarks>
    /// **先写记录再造单据**：记录的插入撞上唯一索引，说明这一期已经有人做过了，
    /// 此时单据尚未产生，退出即可。反过来（先造后记）在两个实例并发时会各造一张。
    ///
    /// 每期一个独立事务 —— 一期失败不该回滚已经成立的其它期。
    /// </remarks>
    private async Task<RecurringRunDto?> GeneratePeriodAsync(
        RecurringDocument template, DateTime period, CancellationToken cancellationToken)
    {
        var autoPost = template.AutoPost ?? _options.DefaultAutoPost;
        var run = new RecurringRun
        {
            RecurringDocumentId = template.Id,
            PeriodDate = period,
            Status = RecurringRunStatus.Generated,
        };

        try
        {
            return await ExecuteInUnitOfWorkAsync(async ct =>
            {
                await _runRepository.InsertAsync(run, ct);
                await _runRepository.SaveChangesAsync(ct);

                var built = await _builder.BuildAsync(template, period, autoPost, ct);
                if (!built.Succeeded)
                {
                    // 造单据失败 -> 整个事务回滚（连同刚插入的记录），失败留痕在事务外补写。
                    throw new RecurringAbortException(Result.Failure(built.Message!, built.Code ?? 400));
                }

                run.DocType = built.Data!.DocType;
                run.DocId = built.Data.DocId;
                run.DocNumber = built.Data.Number;
                run.Posted = built.Data.Posted;
                await _runRepository.UpdateAsync(run, ct);

                return ToDto(run, template.Name);
            }, cancellationToken);
        }
        catch (RecurringAbortException ex)
        {
            UndoFailedInsert(run);
            return await RecordFailureAsync(template, period, ex.Result.Message ?? "Generation failed.", cancellationToken);
        }
        catch (Exception ex) when (IsDuplicatePeriod(ex))
        {
            // 这一期已经有人做过了（重跑或并发）。这正是幂等键该起的作用，不是错误。
            UndoFailedInsert(run);
            Logger?.LogInformation(
                "Recurring template {TemplateId} period {Period:yyyy-MM-dd} was already generated; skipping.",
                template.Id, period);
            return null;
        }
        catch (Exception ex)
        {
            UndoFailedInsert(run);
            Logger?.LogError(ex, "Recurring template {TemplateId} failed for period {Period:yyyy-MM-dd}.", template.Id, period);
            return await RecordFailureAsync(template, period, ex.Message, cancellationToken);
        }
    }

    /// <summary>
    /// 撤销一条<b>插入失败</b>的生成记录。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★ 插入失败后实体仍是 <c>Added</c> 留在变更跟踪器里，会被本 DI 作用域内<b>任何</b>
    /// 后续 <c>SaveChanges</c> 重放。而下一个 <c>SaveChanges</c> 就是 <see cref="AdvanceAsync"/>
    /// 的模板更新 —— 它只接住 <c>DbUpdateConcurrencyException</c>，于是重放出来的
    /// <c>DbUpdateException</c> 会冲出 <see cref="SweepAsync"/>，把本轮<b>剩下的模板全部弄死</b>。
    /// </para>
    /// <para>
    /// 触发它的不是什么异常情形，而是本模块设计上的<b>正常</b>路径：
    /// 「这一期已经有人做过了」（重跑或多实例并发）—— 也就是说，正是那道让多实例安全的
    /// 唯一索引，在没有撤销的情况下会毁掉整轮扫描。
    /// </para>
    /// <para>
    /// ★ 三处 catch 的注释都写着「不能再抛，否则扫描会在第一条坏模板上整个停摆」，
    /// 而不撤销等于两行之后照样停摆 —— 吞掉异常只挡住了症状的第一跳。
    /// 手法与 <c>DocumentNumberService</c> 的首插竞态兜底一致（<c>Remove</c> 一个 <c>Added</c>
    /// 实体即把它转为 <c>Detached</c>，不会产生任何 DELETE 语句）。
    /// </para>
    /// </remarks>
    private void UndoFailedInsert(RecurringRun run) => _runRepository.Discard(run);

    private async Task<RecurringRunDto?> RecordSkippedAsync(RecurringDocument template, DateTime period, CancellationToken cancellationToken)
    {
        var run = new RecurringRun
        {
            RecurringDocumentId = template.Id,
            PeriodDate = period,
            Status = RecurringRunStatus.Skipped,
            FailReason = $"Skipped by the '{_options.CatchUpPolicy}' catch-up policy.",
        };

        try
        {
            await _runRepository.InsertAsync(run, cancellationToken);
            await _runRepository.SaveChangesAsync(cancellationToken);
            return ToDto(run, template.Name);
        }
        catch (Exception ex) when (IsDuplicatePeriod(ex))
        {
            UndoFailedInsert(run);
            return null;
        }
    }

    /// <summary>
    /// 失败留痕。
    /// </summary>
    /// <remarks>
    /// 失败行**不占幂等键**（唯一索引排除 Failed），所以下一次扫描会重试这一期 ——
    /// 停用的科目被启用回来之后，账单自己会补上。
    /// </remarks>
    private async Task<RecurringRunDto?> RecordFailureAsync(
        RecurringDocument template, DateTime period, string reason, CancellationToken cancellationToken)
    {
        var run = new RecurringRun
        {
            RecurringDocumentId = template.Id,
            PeriodDate = period,
            Status = RecurringRunStatus.Failed,
            FailReason = Truncate(reason, 1000),
        };

        try
        {
            await _runRepository.InsertAsync(run, cancellationToken);
            await _runRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // 连失败记录都写不进去时不能再抛：那会让扫描在第一条坏模板上整个停摆。
            // ★ 但只吞不撤销挡不住停摆：留在跟踪器里的 Added 实体会被下一次
            //   SaveChanges 重放，见 UndoFailedInsert。
            UndoFailedInsert(run);
            Logger?.LogError(ex, "Could not record the failed run for template {TemplateId}.", template.Id);
            return null;
        }

        return ToDto(run, template.Name);
    }

    /// <summary>
    /// 推进排期。
    /// </summary>
    /// <remarks>
    /// ★**无论这一轮成功与否都要推进**，否则一条永远失败的模板会在每次扫描里重试
    /// 同一期，把生成记录表刷满而别的模板照样跑不动。失败的那一期由
    /// <see cref="ResolveRetriesAsync"/> 单独捡回来重试（失败行不占幂等键），
    /// 所以推进排期不会把它弄丢。
    ///
    /// 到达结束日或次数上限时置 Ended：一条已经不会再产出任何东西的模板，还挂在
    /// "运行中"里只会让人每个月都要重新判断一次它是不是坏了。
    /// </remarks>
    private async Task AdvanceAsync(RecurringDocument template, DateTime today, CancellationToken cancellationToken)
    {
        var tracked = await _repository.GetAsync(template.Id, cancellationToken);
        if (tracked == null)
            return;

        var next = tracked.NextRunDate;
        var guard = 0;
        while (next <= today && guard++ < _options.MaxCatchUpPerRun + 1)
            next = _schedule.Next(next, tracked.Frequency, tracked.Interval, tracked.AnchorDay);

        var generated = await _runRepository
            .Where(r => r.RecurringDocumentId == tracked.Id && r.Status == RecurringRunStatus.Generated)
            .CountAsync(cancellationToken);

        tracked.NextRunDate = next;
        tracked.OccurrenceCount = generated;
        tracked.LastRunDate = today;

        var reachedEnd = tracked.EndDate.HasValue && next > tracked.EndDate.Value;
        var reachedCap = tracked.MaxOccurrences.HasValue && generated >= tracked.MaxOccurrences.Value;
        if (reachedEnd || reachedCap)
            tracked.Status = RecurringStatus.Ended;

        try
        {
            await _repository.UpdateAsync(tracked, cancellationToken: cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 另一个实例同时扫到了这条模板并先推进了排期（模板带并发标记，后写的那个必然撞上）。
            // 赢家写的是同一份推进结果，单据本身由生成记录的唯一索引兜住，这里没有东西可补；
            // 而抛出去会让本轮剩下的模板一个都跑不了，那正是"多实例安全"要避免的。
            Logger?.LogInformation(
                "Recurring template {TemplateId} was advanced concurrently by another instance; leaving the winner's schedule in place.",
                tracked.Id);
        }
    }

    /// <summary>
    /// 判定"这一期已经存在"。
    /// </summary>
    /// <remarks>
    /// 唯一索引的违例在不同数据库上是不同的异常类型，框架已把这层差异收口，
    /// 这里只是问它。
    /// </remarks>
    private static bool IsDuplicatePeriod(Exception ex)
        => ex is DbUpdateException dbEx && dbEx.IsUniqueConstraintViolation();

    private static string Truncate(string text, int max)
        => string.IsNullOrEmpty(text) || text.Length <= max ? text : text[..max];

    private static RecurringRunDto ToDto(RecurringRun run, string? templateName) => new()
    {
        Id = run.Id,
        RecurringDocumentId = run.RecurringDocumentId,
        RecurringDocumentName = templateName,
        PeriodDate = run.PeriodDate,
        Status = run.Status,
        DocType = run.DocType,
        DocId = run.DocId,
        DocNumber = run.DocNumber,
        Posted = run.Posted,
        FailReason = run.FailReason,
        CreationTime = run.CreationTime,
    };
}
