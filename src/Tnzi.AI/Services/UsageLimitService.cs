namespace Tnzi.AI.Services;

/// <summary>
/// <see cref="IUsageLimitService"/> 的默认实现：每个用户一行 <see cref="AiUsageMeter"/>，全部写入是条件 <c>ExecuteUpdate</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>并发</b>：开窗口与判上限是同一条语句（<c>WHERE 用量 &lt; 上限</c> + <c>开始时间 = COALESCE(开始时间, now)</c>），
/// 结算是一条原子累加，跨界重置是以「库里仍是过期的那个开始时间」为条件的更新（先到的执行，后到的匹配 0 行）。
/// 读改写一处都没有：两轮并发结算时，读改写会丢掉其中一轮。
/// </para>
/// <para>
/// <b>事务</b>：裸 SQL 之前先 <c>EnsureTransactionStartedAsync</c>，让它们与请求的工作单元落在同一个物理事务里（新插的计量行要先 flush，
/// 否则条件更新看不见它；预留随请求回滚而结算不回滚，会把用量记到一轮没发生的预留上）。
/// </para>
/// </remarks>
public class UsageLimitService : ApplicationService, IUsageLimitService
{
    private readonly IRepository<AiUsageMeter, Guid> _meterRepository;
    private readonly IUsagePolicyService _policyService;
    private readonly IOptionsMonitor<UsageLimitOptions> _options;
    private readonly TimeProvider _timeProvider;

    public UsageLimitService(
        IRepository<AiUsageMeter, Guid> meterRepository,
        IUsagePolicyService policyService,
        IOptionsMonitor<UsageLimitOptions> options,
        IServiceProvider serviceProvider,
        TimeProvider? timeProvider = null)
        : base(serviceProvider)
    {
        _meterRepository = Check.NotNull(meterRepository);
        _policyService = Check.NotNull(policyService);
        _options = Check.NotNull(options);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    /// <inheritdoc />
    public async Task<UsageLimitDenialDto?> CheckAsync(Guid userId, CancellationToken ct = default)
    {
        var policy = await _policyService.ResolveAsync(userId, ct);
        if (policy.SessionLimitUsd is null && policy.WeeklyLimitUsd is null)
        {
            return null;
        }

        var meter = await ReadMeterAsync(userId, ct);
        var status = BuildStatus(meter, policy, UtcNow);
        return Deny(status);
    }

    /// <inheritdoc />
    public async Task<Result<UsageLimitReservation>> BeginAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            await _meterRepository.EnsureTransactionStartedAsync(ct);
            await GetOrCreateMeterAsync(userId, ct);

            var now = UtcNow;
            await RollExpiredWindowsAsync(userId, now, ct);

            var policy = await _policyService.ResolveAsync(userId, ct);
            var sessionLimit = policy.SessionLimitUsd;
            var weeklyLimit = policy.WeeklyLimitUsd;

            // 判上限与开窗口是同一条语句：两轮并发开始时不会都看到「还没超」而各自开出一个窗口
            var query = _meterRepository.AsQueryable().Where(m => m.UserId == userId);
            if (sessionLimit is { } session)
            {
                query = query.Where(m => m.SessionCostUsd < session);
            }

            if (weeklyLimit is { } weekly)
            {
                query = query.Where(m => m.WeeklyCostUsd < weekly);
            }

            var started = await query.ExecuteUpdateAsync(s => s
                .SetProperty(m => m.SessionStartedAt, m => m.SessionStartedAt ?? now)
                .SetProperty(m => m.WeeklyStartedAt, m => m.WeeklyStartedAt ?? now)
                .SetProperty(m => m.LastUsedAt, now), ct);

            if (started > 0)
            {
                return Ok(new UsageLimitReservation(userId, now));
            }

            var meter = await ReadMeterAsync(userId, ct);
            if (meter is null)
            {
                Logger.LogError("Usage meter for user {UserId} is missing right after it was created; the turn was not started", userId);
                return Fail<UsageLimitReservation>(
                    "The usage meter is unavailable, so the turn could not be started. This is not a usage limit.",
                    500, ErrorCodes.UsageLimitCheckFailed);
            }

            var status = BuildStatus(meter, policy, now);
            var denial = Deny(status);
            if (denial is null)
            {
                // 两项都还有余量却没写上：并发方在条件更新与回读之间改了这一行。不是上限，重试即可。
                Logger.LogWarning("Usage limit check for user {UserId} lost a concurrent update", userId);
                return Fail<UsageLimitReservation>(
                    "The usage limit check lost a concurrent update. This is not a usage limit; please try again.",
                    409, ErrorCodes.UsageLimitCheckFailed);
            }

            await PublishReachedAsync(userId, status, denial);
            return Fail<UsageLimitReservation>(DenialMessage(denial), 429, ErrorCodes.UsageLimitReached, denial);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Error starting a metered turn for user {UserId}", userId);
            return Fail<UsageLimitReservation>("The usage limit could not be checked.", 500, ErrorCodes.UsageLimitCheckFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result> SettleAsync(UsageLimitReservation reservation, decimal costUsd, CancellationToken ct = default)
    {
        Check.NotNull(reservation);

        if (costUsd <= 0m)
        {
            return Ok();
        }

        var userId = reservation.UserId;
        try
        {
            await _meterRepository.EnsureTransactionStartedAsync(ct);

            var now = UtcNow;
            await RollExpiredWindowsAsync(userId, now, ct);

            // 越线事件要比较加量前后，先读一份未跟踪的快照
            var before = await ReadMeterAsync(userId, ct);

            var updated = await _meterRepository.AsQueryable()
                .Where(m => m.UserId == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.SessionCostUsd, m => m.SessionCostUsd + costUsd)
                    .SetProperty(m => m.WeeklyCostUsd, m => m.WeeklyCostUsd + costUsd)
                    // 这一轮跑着的时候窗口过期了：费用记进从现在开的新窗口
                    .SetProperty(m => m.SessionStartedAt, m => m.SessionStartedAt ?? now)
                    .SetProperty(m => m.WeeklyStartedAt, m => m.WeeklyStartedAt ?? now)
                    .SetProperty(m => m.LastUsedAt, now), ct);

            if (updated == 0)
            {
                Logger.LogWarning("Usage meter for user {UserId} is missing; {Cost} USD was not recorded", userId, costUsd);
                return Fail("The usage meter is unavailable; the cost was not recorded.", 500, ErrorCodes.UsageLimitCheckFailed);
            }

            if (before is not null)
            {
                await PublishThresholdsCrossedAsync(userId, before, costUsd, now, ct);
            }

            return Ok();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Error recording {Cost} USD of usage for user {UserId}", costUsd, userId);
            return Fail("The usage could not be recorded.", 500, ErrorCodes.UsageLimitCheckFailed);
        }
    }

    /// <inheritdoc />
    public async Task<UsageLimitStatusDto> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var policy = await _policyService.ResolveAsync(userId, ct);
        var meter = await ReadMeterAsync(userId, ct);
        return BuildStatus(meter, policy, UtcNow);
    }

    /// <inheritdoc />
    public async Task<MyUsageLimitsDto> GetMyStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var status = await GetStatusAsync(userId, ct);
        return new MyUsageLimitsDto
        {
            WarningThreshold = status.WarningThreshold,
            CheckedAt = status.CheckedAt,
            Windows = status.Windows.Select(w => new MyUsageWindowDto
            {
                Window = w.Window,
                Limited = w.LimitUsd is not null,
                DurationMinutes = w.DurationMinutes,
                UsedPercent = w.UsedPercent,
                StartedAt = w.StartedAt,
                ResetsAt = w.ResetsAt,
                Exceeded = w.Exceeded,
                WarningReached = w.WarningReached
            }).ToList()
        };
    }

    /// <inheritdoc />
    public async Task<Result<IPagedList<UserUsageSummaryDto>>> GetUsersAsync(UsageMeterQueryDto query, CancellationToken ct = default)
    {
        Check.NotNull(query);

        // 不跟踪地读：本作用域里新插的计量行若还被跟踪，跟踪查询会交回插入时那份（条件更新改的是库，不是它）
        var page = await _meterRepository.AsQueryable()
            .WhereIf(m => m.UserId == query.UserId!.Value, query.UserId.HasValue)
            .OrderByDescending(m => m.LastUsedAt)
            .ThenBy(m => m.UserId)
            .CreateAsync(query);

        var limits = await _policyService.ResolveLimitsAsync(page.Items.Select(m => m.UserId).ToList(), ct);
        var options = _options.CurrentValue;
        var now = UtcNow;

        var items = page.Items.Select(meter =>
        {
            var (custom, session, weekly) = limits.GetValueOrDefault(meter.UserId);
            return new UserUsageSummaryDto
            {
                UserId = meter.UserId,
                HasCustomPolicy = custom,
                LastUsedAt = meter.LastUsedAt,
                Windows =
                [
                    UsageWindowClock.Status(UsageLimitWindow.Session, meter.SessionStartedAt, meter.SessionCostUsd,
                        options.SessionWindow, session, options.WarningThreshold, now),
                    UsageWindowClock.Status(UsageLimitWindow.Weekly, meter.WeeklyStartedAt, meter.WeeklyCostUsd,
                        options.WeeklyWindow, weekly, options.WarningThreshold, now)
                ]
            };
        }).ToList();

        return Ok<IPagedList<UserUsageSummaryDto>>(new PagedList<UserUsageSummaryDto>(items, page.PageIndex, page.PageSize, page.TotalCount));
    }

    /// <inheritdoc />
    public async Task<Result> ResetAsync(Guid userId, CancellationToken ct = default)
    {
        await _meterRepository.EnsureTransactionStartedAsync(ct);
        await _meterRepository.AsQueryable()
            .Where(m => m.UserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.SessionStartedAt, (DateTime?)null)
                .SetProperty(m => m.SessionCostUsd, 0m)
                .SetProperty(m => m.WeeklyStartedAt, (DateTime?)null)
                .SetProperty(m => m.WeeklyCostUsd, 0m), ct);

        Logger.LogInformation("Reset the usage windows of user {UserId}", userId);
        return Ok();
    }

    #region 窗口

    /// <summary>
    /// 把过期的窗口落到库里：5 小时窗口回到「没开始」，每周窗口滚到包含 <paramref name="now"/> 的周期，用量都归零。
    /// </summary>
    /// <remarks>
    /// 条件里带着读到的那个开始时间：并发的两次重置算出同一个新开始时间，先到的执行，后到的匹配 0 行，
    /// 不会把先到者刚开的窗口或刚记的用量再清一遍。
    /// </remarks>
    private async Task RollExpiredWindowsAsync(Guid userId, DateTime now, CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var sessionCutoff = now - options.SessionWindow;

        await _meterRepository.AsQueryable()
            .Where(m => m.UserId == userId && m.SessionStartedAt != null && m.SessionStartedAt <= sessionCutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.SessionStartedAt, (DateTime?)null)
                .SetProperty(m => m.SessionCostUsd, 0m), ct);

        var weeklyStart = await _meterRepository.AsQueryable()
            .Where(m => m.UserId == userId)
            .Select(m => m.WeeklyStartedAt)
            .FirstOrDefaultAsync(ct);

        if (weeklyStart is { } start && now >= start + options.WeeklyWindow)
        {
            var rolled = UsageWindowClock.RollWeekly(start, options.WeeklyWindow, now);
            await _meterRepository.AsQueryable()
                .Where(m => m.UserId == userId && m.WeeklyStartedAt == start)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.WeeklyStartedAt, (DateTime?)rolled)
                    .SetProperty(m => m.WeeklyCostUsd, 0m), ct);
        }
    }

    private UsageLimitStatusDto BuildStatus(AiUsageMeter? meter, EffectiveUsagePolicyDto policy, DateTime now)
    {
        var options = _options.CurrentValue;
        return new UsageLimitStatusDto
        {
            WarningThreshold = options.WarningThreshold,
            CheckedAt = now,
            LastUsedAt = meter?.LastUsedAt,
            Windows =
            [
                UsageWindowClock.Status(UsageLimitWindow.Session, meter?.SessionStartedAt, meter?.SessionCostUsd ?? 0m,
                    options.SessionWindow, policy.SessionLimitUsd, options.WarningThreshold, now),
                UsageWindowClock.Status(UsageLimitWindow.Weekly, meter?.WeeklyStartedAt, meter?.WeeklyCostUsd ?? 0m,
                    options.WeeklyWindow, policy.WeeklyLimitUsd, options.WarningThreshold, now)
            ]
        };
    }

    /// <summary>
    /// 到上限的窗口；两个都到时报更晚重置的那个（要等到它才能再用），上限为 0 的（不会自己重置）优先。没到上限返回 null。
    /// </summary>
    private static UsageLimitDenialDto? Deny(UsageLimitStatusDto status)
    {
        var blocking = status.Windows
            .Where(w => w.Exceeded)
            .OrderByDescending(w => w.ResetsAt is null)
            .ThenByDescending(w => w.ResetsAt)
            .FirstOrDefault();

        return blocking is null ? null : new UsageLimitDenialDto { Window = blocking.Window, ResetsAt = blocking.ResetsAt };
    }

    private static string DenialMessage(UsageLimitDenialDto denial)
    {
        var window = denial.Window == UsageLimitWindow.Session ? "5-hour" : "weekly";
        return denial.ResetsAt is { } resetsAt
            ? $"You have reached your {window} usage limit. It resets at {resetsAt:yyyy-MM-dd HH:mm} UTC."
            : $"You have reached your {window} usage limit.";
    }

    #endregion

    #region 计量行

    private Task<AiUsageMeter?> ReadMeterAsync(Guid userId, CancellationToken ct)
        => _meterRepository.AsQueryable().FirstOrDefaultAsync(m => m.UserId == userId, ct);

    /// <summary>
    /// 计量行不存在就建一行并当场 flush：随后的条件更新是绕开变更跟踪器的 SQL，只停在跟踪器里的新行它看不见，
    /// 会被读成「到上限了」。并发首插由唯一索引兜底，撞了就丢掉自己这份、用别人插的那行。
    /// </summary>
    private async Task GetOrCreateMeterAsync(Guid userId, CancellationToken ct)
    {
        if (await _meterRepository.AsQueryable().AnyAsync(m => m.UserId == userId, ct))
        {
            return;
        }

        var meter = new AiUsageMeter { UserId = userId };
        try
        {
            await _meterRepository.InsertAsync(meter, ct);
            await _meterRepository.SaveChangesAsync(ct);

            // 落库后不再跟踪它：随后的写入全是绕开变更跟踪器的条件更新，留着这份就是一份越来越旧的副本，
            // 本作用域里任何一次跟踪查询或 SaveChanges 都会拿它说话
            _meterRepository.Discard(meter);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            // 插入失败的实体仍是 Added：不丢掉的话本作用域下一次 SaveChanges 会重放它，异常落在毫不相干的地方
            _meterRepository.Discard(meter);
            Logger.LogDebug("Usage meter for user {UserId} was created by a concurrent request", userId);
        }
    }

    #endregion

    #region 事件

    private async Task PublishReachedAsync(Guid userId, UsageLimitStatusDto status, UsageLimitDenialDto denial)
    {
        var window = status.Windows.First(w => w.Window == denial.Window);
        await PublishSafelyAsync(new UsageLimitReachedEvent
        {
            UserId = userId,
            Window = denial.Window,
            LimitUsd = window.LimitUsd ?? 0m,
            UsedUsd = window.UsedUsd,
            ResetsAt = denial.ResetsAt
        });
    }

    /// <summary>
    /// 这一笔费用让哪个窗口越过了提示阈值或上限，各发一条 <see cref="UsageLimitThresholdReachedEvent"/>。
    /// </summary>
    /// <remarks>
    /// 只在越过的那一次发：加量前已经在线上的不再发。越线通常发生在结算而不是开始（实际花费事前不知道），所以只能在这里判。
    /// </remarks>
    private async Task PublishThresholdsCrossedAsync(Guid userId, AiUsageMeter before, decimal costUsd, DateTime now, CancellationToken ct)
    {
        var policy = await _policyService.ResolveAsync(userId, ct);
        var beforeStatus = BuildStatus(before, policy, now);
        var after = new AiUsageMeter
        {
            UserId = userId,
            SessionStartedAt = before.SessionStartedAt ?? now,
            SessionCostUsd = before.SessionCostUsd + costUsd,
            WeeklyStartedAt = before.WeeklyStartedAt ?? now,
            WeeklyCostUsd = before.WeeklyCostUsd + costUsd
        };
        var afterStatus = BuildStatus(after, policy, now);

        foreach (var window in afterStatus.Windows)
        {
            var previous = beforeStatus.Windows.First(w => w.Window == window.Window);
            var crossedLimit = window.Exceeded && !previous.Exceeded;
            var crossedWarning = window.WarningReached && !previous.WarningReached;
            if (!crossedLimit && !crossedWarning)
            {
                continue;
            }

            await PublishSafelyAsync(new UsageLimitThresholdReachedEvent
            {
                UserId = userId,
                Window = window.Window,
                LimitReached = window.Exceeded,
                UsedPercent = window.UsedPercent ?? 0m,
                ResetsAt = window.ResetsAt
            });
        }
    }

    private async Task PublishSafelyAsync<TEvent>(TEvent evt) where TEvent : class, IEvent
    {
        try
        {
            await (EventBus?.PublishAsync(evt) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            // 事件只喂告警与通知，发不出去不该让计量失败
            Logger.LogWarning(ex, "Failed to publish {Event}", typeof(TEvent).Name);
        }
    }

    #endregion
}
