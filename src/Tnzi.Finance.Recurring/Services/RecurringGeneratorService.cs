namespace Tnzi.Finance.Recurring.Services;

/// <summary>
/// 到期生成
/// </summary>
/// <remarks>
/// 三条不变量，缺一条这个模块就不该上生产：
/// <list type="number">
/// <item><b>幂等</b> —— 每一期先写 <see cref="RecurringRun"/> 再造单据，唯一索引兜住
///   重跑与并发。给客户重复开一张发票是要打电话道歉的事故。</item>
/// <item><b>一期失败不拖累其它期</b> —— 每期一个独立 DI 作用域与独立事务，物理上够不到调用方的
///   环境事务；第三期的科目被停用，不该让前两期一起回滚，也不该让第四期不再尝试。</item>
/// <item><b>失败留痕</b> —— 失败同样落记录（不占幂等键，下次重试）。悄悄跳过的
///   那一期，没有人会发现。</item>
/// </list>
/// </remarks>
public class RecurringGeneratorService : ApplicationService, IRecurringGeneratorService
{
    private readonly IRepository<RecurringDocument, Guid> _repository;
    private readonly IRepository<RecurringRun, Guid> _runRepository;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRecurrenceSchedule _schedule;
    private readonly RecurringOptions _options;

    public RecurringGeneratorService(
        IServiceProvider serviceProvider,
        IRepository<RecurringDocument, Guid> repository,
        IRepository<RecurringRun, Guid> runRepository,
        IServiceScopeFactory scopeFactory,
        IRecurrenceSchedule schedule,
        IOptionsSnapshot<RecurringOptions> options)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _runRepository = Check.NotNull(runRepository);
        _scopeFactory = Check.NotNull(scopeFactory);
        _schedule = Check.NotNull(schedule);
        _options = Check.NotNull(options).Value;
    }

    public async Task<Result<RecurringSweepResultDto>> RunDueAsync(DateTime? asOf = null, CancellationToken cancellationToken = default)
    {
        var resolved = ResolveAsOf(asOf);
        if (!resolved.Succeeded)
            return Fail<RecurringSweepResultDto>(resolved.Message!, resolved.Code ?? 400);
        var today = resolved.Data;

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
        var resolved = ResolveAsOf(asOf);
        if (!resolved.Succeeded)
            return Fail<RecurringSweepResultDto>(resolved.Message!, resolved.Code ?? 400);
        var today = resolved.Data;

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
            && (await ResolveRetriesAsync(template, cancellationToken)).Count == 0)
        {
            return Fail<RecurringSweepResultDto>($"Nothing is due yet; the next run is {template.NextRunDate:yyyy-MM-dd}.", 409);
        }

        return await SweepAsync([template], today, cancellationToken);
    }

    /// <summary>
    /// 扫描的「今天」。缺省取注入时钟；显式给的日期不得落在未来（留一天时区余量）。
    /// </summary>
    /// <remarks>
    /// ★ <c>asOf</c> 的正当用途是回补过去，未来值没有任何业务含义 —— 而一次
    /// <c>run-due?asOf=2099-01-01</c>（或运维脚本把年份打错）会对每一条 Active 模板生成
    /// <see cref="RecurringOptions.MaxCatchUpPerRun"/> 张日期落在未来的单据（AutoPost 部署直接进总账）、
    /// 把排期推进两年，且没有任何撤销路径。与 <c>LedgerLockService</c> 的封账日守卫同一口径：
    /// 这是最容易被手滑打成错误年份的那种输入。
    /// </remarks>
    private Result<DateTime> ResolveAsOf(DateTime? asOf)
    {
        var now = TimeProvider.GetUtcNow().UtcDateTime.ToUtcDate();
        if (asOf == null)
            return Result.Success(now);

        var requested = asOf.Value.ToUtcDate();
        if (requested > now.AddDays(1))
            return Result.Failure<DateTime>("The as-of date cannot be more than one day in the future.", 400);

        return Result.Success(requested);
    }

    private async Task<Result<RecurringSweepResultDto>> SweepAsync(
        List<RecurringDocument> templates, DateTime today, CancellationToken cancellationToken)
    {
        var result = new RecurringSweepResultDto { TemplatesDue = templates.Count };

        if (UnitOfWorkManager?.IsEnabledTransaction == true)
        {
            // 每期在自己的作用域里提交，所以调用方的事务回滚**不会**撤销已生成的单据 ——
            // 但它也管不住已经生成的东西。说出来，免得有人以为「外层回滚 = 什么都没发生」。
            Logger?.LogWarning(
                "Recurring sweep is running inside an ambient unit of work (depth {Depth}); each period commits in its own scope and will not be rolled back with the caller.",
                UnitOfWorkManager.TransactionDepth);
        }

        foreach (var template in templates)
        {
            // 失败过的期次先补（它们是更早的义务），且**占用次数额度**：一份「只开一期」的订阅，
            // 第一期失败、第二期到期时若两张都生成，客户就收到两张发票。
            var retries = await ResolveRetriesAsync(template, cancellationToken);
            var periods = ResolvePeriods(template, today, reserved: retries.Count, out var skipped, out var nextRunDate);

            foreach (var period in skipped)
            {
                var run = await RecordSkippedAsync(template, period, cancellationToken);
                if (run != null)
                {
                    result.Skipped++;
                    result.Runs.Add(run);
                }
            }

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

            await AdvanceAsync(template, today, nextRunDate, cancellationToken);
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
    /// <param name="template">模板</param>
    /// <param name="today">扫描的「今天」</param>
    /// <param name="reserved">本轮已被重试占去的次数额度</param>
    /// <param name="skipped">被补齐策略排除、要留痕的期次</param>
    /// <param name="nextRunDate">
    /// ★ 第一个<b>没有</b>被本轮消费的期次 —— 排期推进必须落在这里而不是另走一遍日历：
    /// 两处各自走一遍，步数差一就丢一期（曾经就是：生成 P1..P24 而排期推到 P26，P25 既无 Generated 也无
    /// Skipped 行，重试只捡 Failed 行，于是永久消失且零症状）。上限绑定时它落在今天之前，下一轮接着补。
    /// </param>
    private List<DateTime> ResolvePeriods(RecurringDocument template, DateTime today, int reserved, out List<DateTime> skipped, out DateTime nextRunDate)
    {
        var all = new List<DateTime>();
        var cursor = template.NextRunDate;
        var remaining = template.MaxOccurrences.HasValue
            ? Math.Max(0, template.MaxOccurrences.Value - template.OccurrenceCount - reserved)
            : int.MaxValue;

        while (cursor <= today && all.Count < _options.MaxCatchUpPerRun && all.Count < remaining)
        {
            if (template.EndDate.HasValue && cursor > template.EndDate.Value)
                break;
            all.Add(cursor);
            cursor = _schedule.Next(cursor, template.Frequency, template.Interval, template.AnchorDay);
        }

        nextRunDate = cursor;
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
    /// <para>
    /// ★ 重试同样受 <c>MaxOccurrences</c> 与 <c>EndDate</c> 约束：它们是「再开一次」，不是「白送一次」。
    /// 落在 <c>NextRunDate</c> 之后的失败期次交给到期走查（那是它自己的路），这里只捡排期已经推过的。
    /// </para>
    /// </remarks>
    private async Task<List<DateTime>> ResolveRetriesAsync(RecurringDocument template, CancellationToken cancellationToken)
    {
        if (_options.MaxFailedRetries <= 1)
            return [];

        var remaining = template.MaxOccurrences.HasValue
            ? Math.Max(0, template.MaxOccurrences.Value - template.OccurrenceCount)
            : int.MaxValue;
        if (remaining == 0)
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

        return [.. attempts
            .Where(a => a.Count < _options.MaxFailedRetries
                        && !settledSet.Contains(a.Period)
                        && a.Period < template.NextRunDate
                        && (!template.EndDate.HasValue || a.Period <= template.EndDate.Value))
            .Select(a => a.Period)
            .OrderBy(d => d)
            .Take(Math.Min(_options.MaxCatchUpPerRun, remaining))];
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
    /// <para>
    /// **先写记录再造单据**：记录的插入撞上唯一索引，说明这一期已经有人做过了，
    /// 此时单据尚未产生，退出即可。反过来（先造后记）在两个实例并发时会各造一张。
    /// </para>
    /// <para>
    /// ★★ **每期一个独立 DI 作用域**，在它自己的 DbContext 与工作单元里提交 —— 不是本服务作用域里的
    /// <c>ExecuteInUnitOfWorkAsync</c>。后者在调用方已经开着事务时（宿主 <c>EnableGlobalUnitOfWork</c>、
    /// 或消费方在自己的 UoW 里调 <c>RunDueAsync</c>）是<b>嵌套</b>的：内层「提交」只 flush，而内层异常回滚会把
    /// 环境事务整个撤销。于是第三期失败会让前两期已生成的发票与幂等行一并消失，异常被吞、循环继续，
    /// 此后的写入落在自动提交模式下正常持久化，响应仍报告前两期「已生成」并带着它们的真实编号 ——
    /// 一份报告成功的静默数据丢失。手法与后台扫描的「每租户一个作用域」相同；租户经静态 AsyncLocal 流入新作用域。
    /// </para>
    /// <para>
    /// 失败留痕与跳过留痕仍写在本服务作用域（它们要与调用方一起提交或回滚，那是调用方的事）。
    /// </para>
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

        // 作用域随本期结束而释放：插入失败的 Added 实体连同它的跟踪器一起消失，不会被谁重放。
        using var scope = _scopeFactory.CreateScope();
        var runRepository = scope.ServiceProvider.GetRequiredService<IRepository<RecurringRun, Guid>>();
        var builder = scope.ServiceProvider.GetRequiredService<RecurringDocumentBuilder>();
        var unitOfWork = scope.ServiceProvider.GetService<IUnitOfWorkManager>();

        try
        {
            return await RunInOwnTransactionAsync(unitOfWork, async ct =>
            {
                await runRepository.InsertAsync(run, ct);
                await runRepository.SaveChangesAsync(ct);

                var built = await builder.BuildAsync(template, period, autoPost, ct);
                if (!built.Succeeded)
                {
                    // 造单据失败 -> 本期事务回滚（连同刚插入的记录），失败留痕在事务外补写。
                    throw new RecurringAbortException(Result.Failure(built.Message!, built.Code ?? 400));
                }

                run.DocType = built.Data!.DocType;
                run.DocId = built.Data.DocId;
                run.DocNumber = built.Data.Number;
                run.Posted = built.Data.Posted;
                await runRepository.UpdateAsync(run, ct);

                return ToDto(run, template.Name);
            }, cancellationToken);
        }
        catch (RecurringAbortException ex)
        {
            return await RecordFailureAsync(template, period, ex.Result.Message ?? "Generation failed.", cancellationToken);
        }
        catch (Exception ex) when (IsDuplicatePeriod(ex))
        {
            // 这一期已经有人做过了（重跑或并发）。这正是幂等键该起的作用，不是错误。
            Logger?.LogInformation(
                "Recurring template {TemplateId} period {Period:yyyy-MM-dd} was already generated; skipping.",
                template.Id, period);
            return null;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Recurring template {TemplateId} failed for period {Period:yyyy-MM-dd}.", template.Id, period);
            return await RecordFailureAsync(template, period, ex.Message, cancellationToken);
        }
    }

    /// <summary>
    /// 在<b>给定作用域的</b>工作单元里跑一段写入：成功提交、异常回滚后重抛。
    /// 形状与 <c>ApplicationService.ExecuteInUnitOfWorkAsync</c> 相同，区别只在管理器来自本期自己的作用域 ——
    /// 那正是让它够不到调用方环境事务的全部理由。
    /// </summary>
    private static async Task<TResult> RunInOwnTransactionAsync<TResult>(
        IUnitOfWorkManager? unitOfWork, Func<CancellationToken, Task<TResult>> func, CancellationToken cancellationToken)
    {
        if (unitOfWork == null)
            return await func(cancellationToken);

        unitOfWork.EnableTransaction();
        try
        {
            var result = await func(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            return result;
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// 撤销一条<b>插入失败</b>的生成记录（跳过留痕与失败留痕这两处本作用域内的写入）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★ 插入失败后实体仍是 <c>Added</c> 留在变更跟踪器里，会被本 DI 作用域内<b>任何</b>
    /// 后续 <c>SaveChanges</c> 重放。而下一个 <c>SaveChanges</c> 就是 <see cref="AdvanceAsync"/>
    /// 的模板更新 —— 它只接住 <c>DbUpdateConcurrencyException</c>，于是重放出来的
    /// <c>DbUpdateException</c> 会冲出 <see cref="SweepAsync"/>，把本轮<b>剩下的模板全部弄死</b>。
    /// 生成本身自 2026-09-12 起在每期自己的作用域里做，那条路径的失败实体随作用域释放而消失，不再经这里。
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
    ///
    /// ★ 新的 <c>NextRunDate</c> 是 <see cref="ResolvePeriods"/> 交出来的「第一个没被本轮消费的期次」，
    /// 不在这里另走一遍日历 —— 两处各走一遍、步数差一就丢一期。补齐上限绑定时它落在今天之前，
    /// 下一轮从那里接着补，没有哪一期会既无 Generated 也无 Skipped 行。
    /// </remarks>
    private async Task AdvanceAsync(RecurringDocument template, DateTime today, DateTime nextRunDate, CancellationToken cancellationToken)
    {
        var tracked = await _repository.GetAsync(template.Id, cancellationToken);
        if (tracked == null)
            return;

        var next = nextRunDate;

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
