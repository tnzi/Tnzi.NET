namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// 订阅计费引擎（partial）：off-session 扣款、支付完成/失败回流状态机、
/// 后台续费/试用转正/过期/暂停恢复/续费提醒扫描、PastDue 催款。
/// 与 SubscriptionService.cs 共享字段与 CalculateNextBillingTime 等私有成员。
/// </summary>
public partial class SubscriptionService
{
    public async Task<Result<int>> RenewExpiredSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var lockUntil = now.AddMinutes(PaymentOptions.BillingLockMinutes);
        var subscriptionOptions = SubscriptionOptions;

        // 到期且自动续费的订阅（含上轮失败降级 PastDue、且到了下一次重试时间的），分页 + 锁过滤。
        // 只取 Id：每一条在抢到计费锁之后重读，见循环里的说明。
        var dueSubscriptions = await WhereDueForRenewal(_subscriptionRepository.AsNoTracking(), now, subscriptionOptions)
            .Where(s => s.BillingLockedUntil == null || s.BillingLockedUntil < now)
            .OrderBy(s => s.NextBillingTime)
            .Take(BillingScanPageSize)
            .Select(s => new { s.Id, s.SubscriptionNo })
            .ToListAsync(cancellationToken);

        if (dueSubscriptions.Count == 0)
            return Ok(0);

        var processed = 0;
        foreach (var due in dueSubscriptions)
        {
            try
            {
                // 多实例原子抢占：抢到才处理，避免重复扣款
                if (!await TryClaimAsync(due.Id, now, lockUntil, cancellationToken))
                    continue;

                // ★ 抢到锁之后按库里此刻的样子重读、重新判定，而不是用这一页读出来时的快照。
                // 快照与抢锁之间（前面每一条都可能带一次渠道往返）这一条可能已被换了计划（另一个实例的到期变更结算、
                // 用户一次补差为 0 的立即升级）、被取消、或已被别的实例续上：拿快照去扣，就是按旧价续新计划一整期，
                // 或对一条已经不该扣的订阅扣款。
                var subscription = await WhereDueForRenewal(_subscriptionRepository.AsNoTracking().Where(s => s.Id == due.Id), now, subscriptionOptions)
                    .Include(s => s.Plan)
                    .FirstOrDefaultAsync(cancellationToken);

                if (subscription == null)
                {
                    await ReleaseBillingLockAsync(due.Id, cancellationToken);
                    continue;
                }

                if (subscription.Plan == null)
                {
                    // 静默跳过等于这条订阅从此再也不被续费，而没有任何迹象。
                    // 计划被硬删或数据迁移出错都会走到这里，两者都要有人知道。
                    Logger.LogError(
                        "Skipping renewal for {SubscriptionNo}: plan {PlanId} could not be loaded. "
                        + "This subscription will never renew until the plan exists again.",
                        subscription.SubscriptionNo, subscription.PlanId);
                    await ReleaseBillingLockAsync(due.Id, cancellationToken);
                    continue;
                }

                // ★ 先结算到期的待生效变更，再扣款。降级约定的就是「本周期末生效」，
                // 而本周期末正是这一刻 —— 顺序反过来，用户在自己已经降级的那一期
                // 仍被按旧价收钱，而订阅详情页此后显示的是新计划（账单与页面对不上，
                // 且没有任何一条日志或状态能看出发生过这件事）。
                // 持锁期间第六条扫描与用户换计划都抢不到这条订阅，这里结算的就是最后一次变更。
                var appliedPlan = await ApplyDuePlanChangeForSubscriptionAsync(subscription.Id, now, cancellationToken);
                var effectivePlan = appliedPlan ?? subscription.Plan;
                var billed = appliedPlan == null
                    ? subscription
                    : await ReloadAfterPlanChangeAsync(subscription, cancellationToken);

                // ★ 转正扣款失败落到 PastDue 的试用，在这里被接着重试时仍是一次转正（现价 − 试用折扣、写转正时间、
                // 发转正事件），与 RetryBillingInternalAsync 同一判据。此前一律按续费全价扣：折扣丢了，
                // TrialConvertedTime 永远为空，挂在转正事件上的授权逻辑也从不触发。
                if (IsUnconvertedTrial(billed))
                {
                    await ChargeTrialConversionAsync(billed, effectivePlan, now, cancellationToken);
                }
                else
                {
                    // 发起 off-session 扣款；成功 → 发 PaymentCompletedEvent → 处理器推进周期；
                    // 失败/无支付方式 → 降级 PastDue（见 ApplyPaymentFailedAsync）
                    await ChargeSubscriptionAsync(
                        billed, SubscriptionBillingPurpose.Renewal, ResolvePlanPrice(billed, effectivePlan), cancellationToken, effectivePlan);
                }

                processed++;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Subscription renewal failed. SubscriptionNo: {SubscriptionNo}", due.SubscriptionNo);
            }
        }

        Logger.LogInformation("Processed renewal for {Count} due subscriptions", processed);
        return Ok(processed);
    }

    /// <summary>
    /// 续费扫描选中一条订阅的条件（不含计费锁）：自动续费、在跑（Active / PastDue）、已到期，
    /// 且 PastDue 已到下一次重试时间。扫描取一页与抢到锁之后的重新判定用的是同一个条件。
    /// </summary>
    /// <remarks>
    /// PastDue 的重试节奏放在 SQL 里而不是取回来再筛：它们的 NextBillingTime 最早，按它升序取的一页
    /// 会先被「还没到重试时间」的行占满，正常到期的续费就此排不上队。
    /// </remarks>
    private static IQueryable<Subscription> WhereDueForRenewal(IQueryable<Subscription> query, DateTime now, SubscriptionOptions options)
        => query
            .Where(s => s.AutoRenew
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue)
                && s.NextBillingTime != null
                && s.NextBillingTime <= now)
            .Where(BuildRenewalRetryDuePredicate(now, options));

    public async Task<Result<int>> ConvertDueTrialsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var lockUntil = now.AddMinutes(PaymentOptions.BillingLockMinutes);

        var dueTrials = await WhereTrialDue(_subscriptionRepository.AsNoTracking(), now)
            .Where(s => s.BillingLockedUntil == null || s.BillingLockedUntil < now)
            .OrderBy(s => s.TrialEndTime)
            .Take(BillingScanPageSize)
            .Select(s => new { s.Id, s.SubscriptionNo })
            .ToListAsync(cancellationToken);

        if (dueTrials.Count == 0)
            return Ok(0);

        var processed = 0;
        foreach (var due in dueTrials)
        {
            try
            {
                if (!await TryClaimAsync(due.Id, now, lockUntil, cancellationToken))
                    continue;

                // 与续费扫描同形：抢到锁之后重读、重新判定（快照之后它可能已被取消、暂停或换了计划）
                var subscription = await WhereTrialDue(_subscriptionRepository.AsNoTracking().Where(s => s.Id == due.Id), now)
                    .Include(s => s.Plan)
                    .FirstOrDefaultAsync(cancellationToken);

                if (subscription == null)
                {
                    await ReleaseBillingLockAsync(due.Id, cancellationToken);
                    continue;
                }

                if (!subscription.AutoRenew || subscription.Plan == null)
                {
                    // 试用到期且不自动续费 → 直接过期
                    await ExpireSubscriptionAsync(subscription.Id, now, cancellationToken);
                }
                else
                {
                    // ★ 与续费扫描同形：先结算到期的待生效变更，再按新生效的计划扣款。
                    // 试用期内提出的降级到期日就是试用截止日，而这一刻正是试用截止。顺序反过来，
                    // 第一个付费周期跑在新计划上、却按旧计划的价格收（降级多收、约定升级少收），
                    // 随后第六条扫描把计划换掉，账单与订阅详情页对不上且没有任何日志能看出来。
                    var appliedPlan = await ApplyDuePlanChangeForSubscriptionAsync(subscription.Id, now, cancellationToken);
                    var effectivePlan = appliedPlan ?? subscription.Plan;
                    var billed = appliedPlan == null
                        ? subscription
                        : await ReloadAfterPlanChangeAsync(subscription, cancellationToken);

                    // 试用转正扣款（计划现价 − 试用折扣）；成功 → 转为 Active；失败 → PastDue
                    await ChargeTrialConversionAsync(billed, effectivePlan, now, cancellationToken);
                }

                processed++;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Trial conversion failed. SubscriptionNo: {SubscriptionNo}", due.SubscriptionNo);
            }
        }

        Logger.LogInformation("Processed trial conversion for {Count} subscriptions", processed);
        return Ok(processed);
    }

    /// <summary>试用转正扫描选中一条订阅的条件（不含计费锁）：仍在试用、试用已到期。</summary>
    private static IQueryable<Subscription> WhereTrialDue(IQueryable<Subscription> query, DateTime now)
        => query.Where(s => s.Status == SubscriptionStatus.Trial
            && s.TrialEndTime != null
            && s.TrialEndTime <= now);

    public async Task<Result<int>> ExpireOverdueSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var subscriptionOptions = SubscriptionOptions;

        var lockUntil = now.AddMinutes(PaymentOptions.BillingLockMinutes);

        var overdueIds = await WhereOverdue(_subscriptionRepository.AsNoTracking(), now, subscriptionOptions)
            // ★ 扣款在途（计费锁未过期）的不过期：钱随后到账时撞上已过期的订阅，只剩一行孤儿付款告警、要人工退款。
            // 扣款落定（成功推进 / 失败降级 / 支付过期）都会释放这把锁，之后照常判定。
            .Where(s => s.BillingLockedUntil == null || s.BillingLockedUntil < now)
            .OrderBy(s => s.NextBillingTime)
            .Take(BillingScanPageSize)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (overdueIds.Count == 0)
            return Ok(0);

        var expired = 0;
        foreach (var id in overdueIds)
        {
            try
            {
                // 与续费扫描抢同一把锁：取数之后另一个实例可能刚对它发起扣款（逾期的 PastDue 仍在重试节奏里）
                if (!await TryClaimAsync(id, now, lockUntil, cancellationToken))
                    continue;

                // ★ 抢到锁之后重新判定：取出这一页之后它可能已被续上、被用户重新打开自动续费或恢复。
                // 此前抢到就过期，一条刚续费成功（锁随回流放掉）的订阅照样被过期掉。
                if (!await WhereOverdue(_subscriptionRepository.AsNoTracking().Where(s => s.Id == id), now, subscriptionOptions).AnyAsync(cancellationToken))
                {
                    await ReleaseBillingLockAsync(id, cancellationToken);
                    continue;
                }

                await ExpireSubscriptionAsync(id, now, cancellationToken);
                expired++;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Subscription expiry failed. SubscriptionId: {SubscriptionId}", id);
            }
        }

        Logger.LogInformation("Expired {Count} overdue subscriptions", expired);
        return Ok(expired);
    }

    /// <summary>
    /// 过期扫描选中一条订阅的条件（不含计费锁）。取一页与抢到锁之后的重新判定用的是同一个条件。
    /// </summary>
    private static IQueryable<Subscription> WhereOverdue(IQueryable<Subscription> query, DateTime now, SubscriptionOptions options)
    {
        var graceLimit = now.AddDays(-options.GracePeriodDays);
        var maxRetry = options.MaxRetryCount;

        return query.Where(s =>
            // 到期未续费（已关闭自动续费，周期自然结束）。
            // ★ 关了自动续费的 Active 同样收：所有进入在跑状态的写入点都已按开关推导 Active / PendingRenewal，
            // 但修复之前落下的这种行既不被续费扫描选中（要求 AutoRenew）、也不在 PendingRenewal 里，付一期用到永远。
            ((s.Status == SubscriptionStatus.PendingRenewal || (s.Status == SubscriptionStatus.Active && !s.AutoRenew))
                && s.NextBillingTime != null && s.NextBillingTime <= now)
            // 逾期欠费超过宽限期。
            // ★ 以宽限期为准，重试上限只决定「还自动扣不扣」：催款信承诺的截止日是 PastDueSince + 宽限期，
            // 而重试按节奏分布在宽限期内（见 BuildRenewalRetryDuePredicate），最后一次重试落在截止日之前。
            // 此前「重试次数用完」也直接过期，叠加没有重试间隔，默认配置约 10 分钟就把订阅停掉。
            // ★ PastDueSince 为 null 时**不**立刻过期：那一列由 ApplyPaymentFailedAsync 写入，
            // 为 null 说明这条订阅是经别的路径进的 PastDue（数据迁移、人工改状态），
            // 「不知道欠了多久」不能读成「欠了很久」—— 那个方向是把宽限期直接跳过，
            // 用户毫无预警地被停服。交给重试上限收口，重试计数同样为 0 时就等下一次扣款失败写上它。
            || (s.Status == SubscriptionStatus.PastDue
                && ((s.PastDueSince != null && s.PastDueSince <= graceLimit)
                    || (s.PastDueSince == null && s.RenewalRetryCount >= maxRetry))));
    }

    public async Task<Result<int>> ResumeDuePausedSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var lockUntil = now.AddMinutes(PaymentOptions.BillingLockMinutes);

        var duePaused = await WherePauseDue(_subscriptionRepository.AsNoTracking(), now)
            .Where(s => s.BillingLockedUntil == null || s.BillingLockedUntil < now)
            .OrderBy(s => s.PausedUntil)
            .Take(BillingScanPageSize)
            .Select(s => new { s.Id, s.SubscriptionNo })
            .ToListAsync(cancellationToken);

        if (duePaused.Count == 0)
            return Ok(0);

        var resumed = 0;
        foreach (var due in duePaused)
        {
            try
            {
                // ★ 与用户的取消 / 恢复抢同一把锁，抢到之后重读、重新判定。此前一次读出一整页跟踪实体，
                // 逐条恢复（可能各带一次渠道扣款）后整行写回：轮到某一条之前它被取消了，写回就把取消撤销、让它重新在跑。
                if (!await TryClaimAsync(due.Id, now, lockUntil, cancellationToken))
                    continue;

                var subscription = await WherePauseDue(_subscriptionRepository.AsNoTracking().Where(s => s.Id == due.Id), now)
                    .FirstOrDefaultAsync(cancellationToken);

                if (subscription == null)
                {
                    await ReleaseBillingLockAsync(due.Id, cancellationToken);
                    continue;
                }

                // 暂停到期的后台恢复不是用户的动作：自动续费开关原样保留（用户可能在暂停期间关掉了它）。
                // 写回时一并放锁。
                await ResumeInternalAsync(subscription, reEnableAutoRenew: false, cancellationToken);
                resumed++;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Subscription auto-resume failed. SubscriptionNo: {SubscriptionNo}", due.SubscriptionNo);
            }
        }

        Logger.LogInformation("Auto-resumed {Count} paused subscriptions", resumed);
        return Ok(resumed);
    }

    /// <summary>暂停到期自动恢复扫描选中一条订阅的条件（不含计费锁）。</summary>
    private static IQueryable<Subscription> WherePauseDue(IQueryable<Subscription> query, DateTime now)
        => query.Where(s => s.Status == SubscriptionStatus.Paused
            && s.PausedUntil != null
            && s.PausedUntil <= now);

    public async Task<Result<int>> SendRenewalRemindersAsync(CancellationToken cancellationToken = default)
    {
        var reminderDays = SubscriptionOptions.AutoRenewalReminderDays;
        if (reminderDays <= 0 || _notificationService == null)
            return Ok(0);

        var now = DateTime.UtcNow;
        var threshold = now.AddDays(reminderDays);

        // 只提醒本周期尚未提醒过的订阅：以 NextBillingTime 作为"周期标识"，
        // 续费成功后计费时间前移，下一周期自然重新具备提醒资格。
        var candidates = await _subscriptionRepository.AsNoTracking()
            .Where(s => s.AutoRenew
                && s.Status == SubscriptionStatus.Active
                && s.NextBillingTime != null
                && s.NextBillingTime > now
                && s.NextBillingTime <= threshold
                // ★ 不按订阅上的邮箱快照筛：收件人在发信时解析（账户联系人 → 快照 → 开通人账号邮箱），
                //   快照为空的订阅（开通时令牌不带邮箱声明）此前一封提醒都收不到，即便账户后来填了联系人
                && (s.RenewalReminderSentFor == null || s.RenewalReminderSentFor != s.NextBillingTime))
            .Include(s => s.Plan)
            .OrderBy(s => s.NextBillingTime)
            .Take(BillingScanPageSize)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return Ok(0);

        var sent = 0;
        foreach (var subscription in candidates)
        {
            try
            {
                if (await SendRenewalReminderAsync(subscription, cancellationToken))
                    sent++;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Renewal reminder failed. SubscriptionNo: {SubscriptionNo}", subscription.SubscriptionNo);
            }
        }

        if (sent > 0)
            Logger.LogInformation("Sent {Count} renewal reminders", sent);

        return Ok(sent);
    }

    /// <summary>
    /// 给一条订阅发本期的续费提醒，返回是否送达。先占下本期的提醒标记再发，没送达就把标记还回去。
    /// </summary>
    /// <remarks>
    /// ★ 只写 <see cref="Subscription.RenewalReminderSentFor"/> 这一列，且条件更新同时重新判定：读出这一页之后被取消、
    /// 关了自动续费或已经续上的，不再提醒；两个实例也不会各发一封。此前逐封同步发完再把读出来的跟踪实体整行写回：
    /// 发信那几分钟里用户的取消被写回成 Active、AutoRenew=true，到期照扣。
    /// 标记的是「针对哪个周期发过」，不是「发过没有」，才能保证每个周期恰好提醒一次。
    /// </remarks>
    private async Task<bool> SendRenewalReminderAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var cycle = subscription.NextBillingTime;
        var previousMark = subscription.RenewalReminderSentFor;

        var claimed = await _subscriptionRepository.AsQueryable()
            .Where(s => s.Id == subscription.Id
                && s.AutoRenew
                && s.Status == SubscriptionStatus.Active
                && s.NextBillingTime == cycle
                && s.RenewalReminderSentFor == previousMark)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RenewalReminderSentFor, cycle), cancellationToken);

        if (claimed == 0)
            return false;

        var delivered = false;
        try
        {
            var upcoming = await ResolveUpcomingChargeAsync(subscription, cycle, cancellationToken);
            // 按币种的小数位写金额：数据库读回的 decimal 带着列的精度（39.0000），JPY 不该写出小数
            var amount = upcoming.Amount.ToString($"F{CurrencyInfo.GetDecimalPlaces(upcoming.Currency)}", CultureInfo.InvariantCulture);
            var hasPaymentMethod = !string.IsNullOrWhiteSpace(subscription.PaymentMethodToken);
            var body = hasPaymentMethod
                ? $"Your {upcoming.PlanName ?? "subscription"} renews on {cycle:yyyy-MM-dd} for {amount} {upcoming.Currency}."
                : $"Your {upcoming.PlanName ?? "subscription"} renews on {cycle:yyyy-MM-dd}, but no payment method is on file. Please add one to avoid interruption.";

            delivered = await SendSubscriptionEmailAsync(subscription, new SubscriptionEmail(
                SubscriptionEmailTemplates.RenewalReminder,
                $"Upcoming renewal for {subscription.SubscriptionNo}",
                body,
                new Dictionary<string, object>
                {
                    ["SubscriptionNo"] = subscription.SubscriptionNo,
                    ["UpcomingPlanName"] = upcoming.PlanName ?? "subscription",
                    ["RenewalDate"] = cycle?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                    ["Amount"] = amount,
                    ["Currency"] = upcoming.Currency,
                    ["HasPaymentMethod"] = hasPaymentMethod,
                    ["PaymentMethod"] = DescribePaymentMethod(subscription)
                }), cancellationToken);
            return delivered;
        }
        finally
        {
            // 没送达（含发信抛异常）：把本期的标记还回去，下一轮重试。只还自己占下的那个值。
            if (!delivered)
            {
                await _subscriptionRepository.AsQueryable()
                    .Where(s => s.Id == subscription.Id && s.RenewalReminderSentFor == cycle)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RenewalReminderSentFor, previousMark), CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// 到 <paramref name="cycle"/> 那一刻续费会按什么计划、收多少、什么币种：与续费扫描同一口径。
    /// </summary>
    /// <remarks>
    /// ★ 续费先结算到期（<c>EffectiveDate &lt;= 周期边界</c>）的待生效变更、再按生效计划的现价扣
    /// （条款与订阅对不上时取快照价，见 <see cref="ResolvePlanPrice"/>；未转正的试用减试用折扣）。
    /// 提醒此前直接写订阅的快照价 <see cref="Subscription.OriginalPrice"/>：运营调价之后、或排好了降级，
    /// 邮件写「将以 29 USD 续费」而 7 天后实际扣 39，或写的是降级前那个计划的价。
    /// 变更的目标计划已不存在时续费会作废那条变更、按当前计划扣，这里同样回到当前计划。
    /// </remarks>
    private async Task<(string? PlanName, decimal Amount, string Currency)> ResolveUpcomingChargeAsync(
        Subscription subscription, DateTime? cycle, CancellationToken cancellationToken)
    {
        var duePlan = cycle == null
            ? null
            : await _changeRepository.AsNoTracking()
                .Where(c => c.SubscriptionId == subscription.Id
                    && c.Status == SubscriptionChangeStatus.Pending
                    && c.EffectiveDate <= cycle.Value)
                .OrderBy(c => c.EffectiveDate)
                .Select(c => c.ToPlan)
                .FirstOrDefaultAsync(cancellationToken);

        // 到期变更结算时把计划的计费条款整套写到订阅上（WritePlanTermsAsync），之后条款必然一致，扣的就是新计划的现价
        if (duePlan != null)
        {
            var price = IsUnconvertedTrial(subscription) ? Math.Max(0m, duePlan.Price - subscription.DiscountAmount) : duePlan.Price;
            return (duePlan.PlanName, price, duePlan.Currency);
        }

        var plan = subscription.Plan;
        if (plan == null)
            return (null, subscription.OriginalPrice, subscription.Currency);

        var amount = IsUnconvertedTrial(subscription)
            ? ResolveTrialConversionAmount(subscription, plan)
            : ResolvePlanPrice(subscription, plan);
        return (plan.PlanName, amount, subscription.Currency);
    }

    public async Task<Result> ApplyPaymentCompletedAsync(SubscriptionPaymentContext context, CancellationToken cancellationToken = default)
    {
        Check.NotNull(context);

        var subscription = await LoadForBillingAsync(context, cancellationToken);
        if (subscription == null)
            return Fail(ErrorCodes.SubscriptionNotFound, 404);

        var now = DateTime.UtcNow;
        var statusBefore = subscription.Status;

        // 幂等：同一支付重复投递（at-least-once / 处理器重试）不重复推进周期
        if (!string.IsNullOrEmpty(context.PaymentTradeNo)
            && string.Equals(subscription.LastBillingTradeNo, context.PaymentTradeNo, StringComparison.Ordinal))
        {
            return Ok();
        }

        // ★ 不信任计费元数据本身：它只是路由键。真正推进状态机之前，付款人必须就是订阅主，
        // 且到账金额不得低于这一次该收的下界 —— 否则任何人拿自己的 0.5 元支付单加一段自填的
        // ExtraData 就能激活 / 续期任意价位的订阅。失败方向关闭：不改订阅行，只记 Warning。
        var trust = VerifyPaymentBelongsToSubscription(subscription, context);
        if (!trust.Succeeded)
            return trust;

        var floor = await ResolveAmountFloorAsync(subscription, context, cancellationToken);
        if (floor.HasValue && context.Amount + CurrencyInfo.FromMinorUnits(1, subscription.Currency) < floor.Value)
        {
            Logger.LogWarning(
                "Ignoring {Purpose} payment {TradeNo} for subscription {SubscriptionNo}: paid {Paid} is below the expected {Expected} {Currency}.",
                context.Purpose, context.PaymentTradeNo, subscription.SubscriptionNo, context.Amount, floor.Value, subscription.Currency);
            return Fail(ErrorCodes.SubscriptionPaymentAmountTooLow, 400);
        }

        // 终态防复活：取消与在途扣款竞态时，已取消/过期的订阅不应被支付完成"复活"并继续扣款。
        // ★ 首期只认 Pending：首期单只为激活一条 Pending 而建，订阅已经不是 Pending（取消了、或已经被另一笔激活）
        // 时到的首期款同样无处可落。此前 Initial 被整个放过、分支里又只处理 Pending，取消之后再付掉的首期款
        // 被安静吸收，订阅仍是 Cancelled、没有任何日志，运营拿不到退款线索。
        // ★ 补缴只认逾期欠费与暂停服务：订阅在补缴单待付期间已经被续上（换卡后的重试先成功了），这笔钱同样无处可落。
        var orphaned = context.Purpose switch
        {
            SubscriptionBillingPurpose.Initial => subscription.Status != SubscriptionStatus.Pending,
            SubscriptionBillingPurpose.Recovery => subscription.Status is not (SubscriptionStatus.PastDue or SubscriptionStatus.Suspended),
            _ => subscription.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired
        };

        if (orphaned)
        {
            Logger.LogWarning(
                "Ignoring {Purpose} payment for non-active subscription {SubscriptionNo} (status={Status}); orphan payment {TradeNo} may require refund.",
                context.Purpose, subscription.SubscriptionNo, subscription.Status, context.PaymentTradeNo);

            if (context.Purpose == SubscriptionBillingPurpose.Proration && context.ChangeId.HasValue)
                await CancelAwaitingChangeAsync(context.ChangeId.Value, cancellationToken);

            if (context.Purpose == SubscriptionBillingPurpose.Recovery
                && string.Equals(subscription.RecoveryPaymentTradeNo, context.PaymentTradeNo, StringComparison.Ordinal))
                subscription.RecoveryPaymentTradeNo = null;

            subscription.BillingLockedUntil = null;
            await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);
            return Ok();
        }

        switch (context.Purpose)
        {
            case SubscriptionBillingPurpose.Initial:
                // 上面的守卫保证此刻是 Pending
                if (subscription.StartTime == default)
                    subscription.StartTime = now;
                subscription.NextBillingTime ??= CalculateNextBillingTime(now, subscription.CycleType, subscription.CycleValue);
                subscription.Status = ResolveRunningStatus(subscription, now);
                subscription.PaidAmount = context.Amount;
                ResetDunning(subscription);
                break;

            case SubscriptionBillingPurpose.Recovery when IsUnconvertedTrial(subscription):
                // 转正扣款失败落下来的逾期 / 暂停：补缴就是那一次转正
                MarkTrialConverted(subscription, now, context.Amount);
                subscription.RecoveryPaymentTradeNo = null;
                await CancelAwaitingChangesForEndedPeriodAsync(subscription.Id, cancellationToken);
                await PublishTrialConvertedAsync(subscription, context);
                break;

            case SubscriptionBillingPurpose.Renewal:
            case SubscriptionBillingPurpose.Recovery:
            {
                // ★ 新的一期从付款那一刻起算：逾期续费的「已付到」早已在过去，暂停服务期间也没有提供服务，都不追收
                var basis = subscription.NextBillingTime ?? now;
                if (basis < now) basis = now; // 逾期续费从当前时间起算
                subscription.NextBillingTime = CalculateNextBillingTime(basis, subscription.CycleType, subscription.CycleValue);
                if (context.Purpose == SubscriptionBillingPurpose.Recovery
                    && string.Equals(subscription.RecoveryPaymentTradeNo, context.PaymentTradeNo, StringComparison.Ordinal))
                    subscription.RecoveryPaymentTradeNo = null;
                // 扣款在途时用户关掉了自动续费：这一期照样归他，到期过期而不是成为一条永不过期的 Active
                subscription.Status = ResolveRunningStatus(subscription, now);
                subscription.PaidAmount = context.Amount;
                ResetDunning(subscription);
                await CancelAwaitingChangesForEndedPeriodAsync(subscription.Id, cancellationToken);
                await PublishRenewedAsync(subscription, context);
                break;
            }

            case SubscriptionBillingPurpose.TrialConversion:
                MarkTrialConverted(subscription, now, context.Amount);
                await CancelAwaitingChangesForEndedPeriodAsync(subscription.Id, cancellationToken);
                await PublishTrialConvertedAsync(subscription, context);
                break;

            case SubscriptionBillingPurpose.Proration:
                await ApplyProrationChangeAsync(subscription, context, now, cancellationToken);
                break;
        }

        // 付款时留存下来的卡（首付 / 补缴）：换成续费用的卡。补缴之前挂着的那张往往正是扣不动的那张，
        // 不换的话，恢复之后的第一次续费照样失败
        if (context.SavedPaymentMethodId.HasValue
            && context.Purpose is SubscriptionBillingPurpose.Initial or SubscriptionBillingPurpose.Recovery)
        {
            var saved = await _paymentMethodService.FindByIdAsync(AccountOf(subscription), context.SavedPaymentMethodId.Value, cancellationToken);
            if (saved != null && string.Equals(saved.ChannelCode, subscription.ChannelCode, StringComparison.OrdinalIgnoreCase))
                ApplyPaymentMethod(subscription, saved);
        }

        if (!string.IsNullOrEmpty(context.PaymentTradeNo))
            subscription.LastBillingTradeNo = context.PaymentTradeNo;
        subscription.BillingLockedUntil = null;
        subscription.SuspendedAt = subscription.Status == SubscriptionStatus.Suspended ? subscription.SuspendedAt : null;
        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

        // 扣款成功回写支付方式的最近使用时间，便于识别长期未用/已失效的卡
        if (subscription.StoredPaymentMethodId.HasValue)
            await _paymentMethodService.MarkUsedAsync(subscription.StoredPaymentMethodId.Value, cancellationToken);

        await PublishStandingTransitionAsync(subscription, statusBefore, context, cancellationToken);

        return Ok();
    }

    public async Task<Result> ApplyPaymentFailedAsync(SubscriptionPaymentContext context, CancellationToken cancellationToken = default)
    {
        Check.NotNull(context);

        var subscription = await LoadForBillingAsync(context, cancellationToken);
        if (subscription == null)
            return Fail(ErrorCodes.SubscriptionNotFound, 404);

        // 失败回流同样只认订阅主自己的支付：否则拿受害者的 SubscriptionNo 建一张不付的单，
        // 等它过期就能零成本把别人的订阅打成 PastDue，三次之后直接过期。
        var trust = VerifyPaymentBelongsToSubscription(subscription, context);
        if (!trust.Succeeded)
            return trust;

        var now = DateTime.UtcNow;
        var shouldNotify = false;
        var statusBefore = subscription.Status;

        // 补缴单没付成（放弃、作废、过期）不是一次扣款失败：不计重试、不发催款信，订阅行清掉那张单的流水号，
        // 并放掉为等它而持有的计费锁。★ 续费扫描看见这张还能付的单时不再扣一笔，而是把锁一直持到它落定
        // （HoldBillingLockUntilSettledAsync，持到单子过期之后再加一个锁时长）；此前这里只清流水号，
        // 那把锁就一直留到自己到期：续费扫描反复「has not settled yet」，过期扫描也跳过它，逾期处理被白白推迟。
        // 只在这张单仍是订阅当前的补缴单时放锁（同一条条件写）：模块自己持锁关单的路径只关首期单，不碰补缴单。
        if (context.Purpose == SubscriptionBillingPurpose.Recovery)
        {
            await _subscriptionRepository.AsQueryable()
                .Where(s => s.Id == subscription.Id && s.RecoveryPaymentTradeNo == context.PaymentTradeNo)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RecoveryPaymentTradeNo, (string?)null)
                    .SetProperty(x => x.BillingLockedUntil, (DateTime?)null), cancellationToken);
            Logger.LogInformation("Recovery payment {TradeNo} for subscription {SubscriptionNo} ended unpaid ({Reason}); the subscription stays {Status}.",
                context.PaymentTradeNo, subscription.SubscriptionNo, context.FailReason, subscription.Status);
            return Ok();
        }

        // 暂停服务的订阅只会被「换卡后当场重试」扣一次（续费扫描不选它）：那一次没扣成，订阅保持暂停，
        // 不回到 PastDue（那会让它重新进入宽限期、恢复服务），也不再计重试或发催款信；放掉那次重试持有的锁，
        // 管理者才能立刻用「立即付款」补缴。
        if (context.Purpose is SubscriptionBillingPurpose.Renewal or SubscriptionBillingPurpose.TrialConversion
            && subscription.Status == SubscriptionStatus.Suspended)
        {
            Logger.LogInformation(
                "Charge {TradeNo} for suspended subscription {SubscriptionNo} failed ({Reason}); it stays suspended until the account pays.",
                context.PaymentTradeNo, subscription.SubscriptionNo, context.FailReason);
            await ReleaseBillingLockAsync(subscription.Id, cancellationToken);
            return Ok();
        }

        // 终态不复活，与完成回流对称：续费 / 转正扣款在渠道侧悬着的那段时间里订阅被取消或过期，那笔扣款随后失败或过期，
        // 此前照样把它改回 PastDue、计一次重试、发一封「请更新支付方式」的催款信；PastDue 还会重新占住同产品的唯一名额，
        // 用户若已重新开通同一产品，这次写回撞上唯一索引，事件重试直至死信。
        // 什么都不写：已结束的订阅不被任何扫描选中，计费锁若被持有也是别人的（恢复正在进行），不能替它放掉。
        if (context.Purpose is SubscriptionBillingPurpose.Renewal or SubscriptionBillingPurpose.TrialConversion
            && subscription.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired)
        {
            Logger.LogInformation(
                "Ignoring a late {Purpose} payment failure for ended subscription {SubscriptionNo} (status={Status}). TradeNo: {TradeNo}, Reason: {Reason}",
                context.Purpose, subscription.SubscriptionNo, subscription.Status, context.PaymentTradeNo, context.FailReason);
            return Ok();
        }

        switch (context.Purpose)
        {
            case SubscriptionBillingPurpose.Renewal:
            case SubscriptionBillingPurpose.TrialConversion:
                subscription.RenewalRetryCount++;
                subscription.PastDueSince ??= now;
                subscription.Status = SubscriptionStatus.PastDue;
                shouldNotify = true;
                Logger.LogWarning(
                    "Subscription billing failed -> PastDue. SubscriptionNo: {SubscriptionNo}, Retry: {Retry}, Reason: {Reason}",
                    subscription.SubscriptionNo, subscription.RenewalRetryCount, context.FailReason);
                break;

            case SubscriptionBillingPurpose.Proration:
                // 不走下面的整行写回：补差没收到只关乎那条变更，订阅行除了计费锁什么都不该动（见 SettleUnpaidProrationAsync）
                await SettleUnpaidProrationAsync(subscription.Id, context, cancellationToken);
                return Ok();

            case SubscriptionBillingPurpose.Initial:
                // 首期单已经以「没收到钱」结束：这条订阅再也激活不了，收口它。
                // 直接返回，不走下面的整行写回：那会拿这份读出来的 Pending 去覆盖并发的激活或取消。
                await EndUnpaidPendingSubscriptionAsync(subscription, context.FailReason, cancellationToken);
                return Ok();
        }

        subscription.BillingLockedUntil = null;
        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

        // 催款：只改状态不通知，用户根本不知道自己被停服了
        if (shouldNotify)
            await SendDunningNoticeAsync(subscription, context.FailReason, cancellationToken);

        await PublishStandingTransitionAsync(subscription, statusBefore, context, cancellationToken);

        return Ok();
    }

    /// <summary>
    /// 首期单已经以「没收到钱」结束（失败 / 作废 / 关单 / 过期）：把还停在 Pending 的订阅终结为 <c>Cancelled</c>，返回是否真的终结了它。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 此前首期失败的回流对 Initial 什么都不做（注释写着「允许用户重试」），可全模块没有为 Pending 重建首期支付的入口：
    /// 重试扣款要求 PastDue、恢复不收 Pending、父模块用户面拒绝建订阅单。于是用户在收银台放弃一次，
    /// 这条 Pending 就永远占着「同产品唯一有效订阅」的名额，再订同一产品得到 400 <c>SUBSCRIPTION_PRODUCT_ALREADY_SUBSCRIBED</c>，
    /// 而他从没付过钱；计划删除守卫与租户扫描也永远把它算作有效订阅。
    /// </para>
    /// <para>
    /// 首期单是唯一能激活它的那一笔，一旦本地以失败 / 过期 / 关闭结束，之后渠道侧再收到钱也只会记成「本地已结束后到账」，
    /// 不再激活任何东西（见 <c>IPaymentService</c> 的终态守卫）。条件更新（<c>WHERE Status = Pending</c>）而不是整行写回：
    /// 读与写之间订阅可能已被激活或取消。终结之后它是一条从没开始过的订阅，恢复会被拒绝，要用就重新开通。
    /// </para>
    /// <para>
    /// ★ 计费锁被持有时不动它。Pending 上的锁只来自一次进行中的用户变更（扫描都不选 Pending），典型是取消：
    /// 取消先关首期单，关单的失败回流在默认的同步事件总线上就在关单调用里跑，在这里终结它，订阅就被宣布取消两次，
    /// 第一次带的还是系统的理由而不是订阅者的。持锁的那次变更自己会收口它；它若没收口（被拒、或是别的变更），
    /// 这条 Pending 在订阅者下次开通同一产品时由 <c>TryEndAbandonedPendingAsync</c> 收口。
    /// </para>
    /// </remarks>
    private async Task<bool> EndUnpaidPendingSubscriptionAsync(Subscription subscription, string? failReason, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var reason = string.IsNullOrWhiteSpace(failReason)
            ? UnpaidFirstPaymentReason
            : $"{UnpaidFirstPaymentReason} {failReason}";
        // 收进列宽；截断点落在代理对中间时整个字符一起舍掉（孤立的高代理项会让严格 UTF-8 的驱动拒绝这次写入）
        reason = reason.FitToLength(CancelReasonMaxLength);

        var ended = await _subscriptionRepository.AsQueryable()
            .Where(s => s.Id == subscription.Id
                && s.Status == SubscriptionStatus.Pending
                && (s.BillingLockedUntil == null || s.BillingLockedUntil < now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SubscriptionStatus.Cancelled)
                .SetProperty(x => x.AutoRenew, false)
                .SetProperty(x => x.EndTime, (DateTime?)now)
                .SetProperty(x => x.CancelTime, (DateTime?)now)
                .SetProperty(x => x.CancelReason, reason)
                .SetProperty(x => x.NextBillingTime, (DateTime?)null)
                .SetProperty(x => x.BillingLockedUntil, (DateTime?)null)
                .SetProperty(x => x.LastModificationTime, (DateTime?)now), cancellationToken);

        if (ended == 0)
            return false;

        Logger.LogInformation(
            "Pending subscription {SubscriptionNo} ended: its first payment was not completed ({Reason}).",
            subscription.SubscriptionNo, failReason);

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new SubscriptionCancelledEvent
            {
                SubscriptionId = subscription.Id,
                SubscriptionNo = subscription.SubscriptionNo,
                UserId = subscription.UserId,
                CancelReason = reason,
                Immediate = true,
                ExpireTime = now
            });
        }

        return true;
    }

    /// <summary>首期单没付成而被系统收口的订阅写在 <see cref="Subscription.CancelReason"/> 里的说明。</summary>
    private const string UnpaidFirstPaymentReason = "First payment not completed.";

    /// <summary><see cref="Subscription.CancelReason"/> 的列宽（见 <c>SubscriptionConfiguration</c>）。</summary>
    private const int CancelReasonMaxLength = 500;

    /// <summary>
    /// 多实例原子抢占：仅当未被锁定或锁已过期时抢到该订阅的计费处理权
    /// </summary>
    private async Task<bool> TryClaimAsync(Guid subscriptionId, DateTime now, DateTime lockUntil, CancellationToken cancellationToken)
    {
        var affected = await _subscriptionRepository.AsQueryable()
            .Where(s => s.Id == subscriptionId && (s.BillingLockedUntil == null || s.BillingLockedUntil < now))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.BillingLockedUntil, lockUntil), cancellationToken);
        return affected > 0;
    }

    /// <summary>
    /// 当场重试一次扣款，不等下一轮扫描：用户手动重试、换卡之后、恢复时已付的那期已经走完。
    /// </summary>
    /// <remarks>
    /// ★ 先抢锁<b>再</b>读，按库里此刻的样子重新判定：仍在跑（Active / PastDue）且已到期才扣，否则放锁不扣。
    /// 调用方手里那份订阅读在抢锁之前，读与抢之间它可能已被取消、或续费已经推进了周期，
    /// 拿那份去扣，就是对一条已取消的订阅扣款，或同一期扣两次。
    /// </remarks>
    private async Task RetryBillingInternalAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (!await TryClaimAsync(subscriptionId, now, now.AddMinutes(PaymentOptions.BillingLockMinutes), cancellationToken))
            return;

        var subscription = await _subscriptionRepository.AsNoTracking()
            .Where(s => s.Id == subscriptionId
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue || s.Status == SubscriptionStatus.Suspended)
                && (s.NextBillingTime == null || s.NextBillingTime <= now))
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(cancellationToken);

        var plan = subscription?.Plan;
        if (subscription == null || plan == null)
        {
            await ReleaseBillingLockAsync(subscriptionId, cancellationToken);
            return;
        }

        // 计划显式传入：这份订阅是游离的只读快照，不会被保存
        if (IsUnconvertedTrial(subscription))
            await ChargeTrialConversionAsync(subscription, plan, now, cancellationToken);
        else
            await ChargeSubscriptionAsync(subscription, SubscriptionBillingPurpose.Renewal, ResolvePlanPrice(subscription, plan), cancellationToken, plan);
    }

    /// <summary>
    /// 到期变更刚把订阅换到新计划上（<see cref="ApplyDuePlanChangeForSubscriptionAsync"/> 写的是数据库那一行）：
    /// 重读一次再扣款。扫描读到的快照里还是旧计划的币种、周期与快照价，拿它去扣新计划，
    /// 会被 <see cref="PlanTermsMatch"/> 判成「条款对不上」而按旧价收。
    /// </summary>
    private async Task<Subscription> ReloadAfterPlanChangeAsync(Subscription snapshot, CancellationToken cancellationToken)
        => await _subscriptionRepository.AsNoTracking().FirstOrDefaultAsync(s => s.Id == snapshot.Id, cancellationToken)
            ?? snapshot;

    /// <summary>
    /// 计划的计费条款（币种、周期）是否仍与订阅的快照一致。
    /// </summary>
    /// <remarks>
    /// 续费按计划现价扣（运营调价随续费生效），币种与下一次计费时间却取订阅的快照。两者对不上时，
    /// 计划现价是另一种币种或另一种周期下的数字：拿它配快照的币种与周期去扣，1500 JPY 的计划价会扣成 1500 USD、
    /// 年费会按月扣。改计划时有存量订户的不许改这两项（见 <c>UpdatePlanAsync</c>），这里兜的是修复之前被改过的计划，
    /// 以及与开通并发的一次改计划。
    /// </remarks>
    private static bool PlanTermsMatch(Subscription subscription, SubscriptionPlan plan)
        => string.Equals(plan.Currency, subscription.Currency, StringComparison.OrdinalIgnoreCase)
            && plan.CycleType == subscription.CycleType
            && plan.CycleValue == subscription.CycleValue;

    /// <summary>
    /// 这一期该按什么价收：计划条款与订阅一致时取计划现价，否则取订阅自己的快照价（<see cref="Subscription.OriginalPrice"/>），
    /// 那是唯一与它的币种、周期配套的数字。
    /// </summary>
    private static decimal ResolvePlanPrice(Subscription subscription, SubscriptionPlan plan)
        => PlanTermsMatch(subscription, plan) ? plan.Price : subscription.OriginalPrice;

    /// <summary>
    /// 开过试用而还没转正：它的下一次扣款是一次试用转正（按「现价 − 试用折扣」收、写转正时间、发转正事件），
    /// 不论是转正扫描、续费扫描对 PastDue 的重试，还是用户换卡后的手动重试发起的。
    /// </summary>
    private static bool IsUnconvertedTrial(Subscription subscription)
        => subscription.TrialEndTime != null && subscription.TrialConvertedTime == null;

    /// <summary>
    /// 续费扫描对 PastDue 订阅的重试节奏：<see cref="SubscriptionOptions.MaxRetryCount"/> 次自动扣款（含第一次失败）
    /// 均匀分布在 <see cref="SubscriptionOptions.GracePeriodDays"/> 内，第 k 次失败之后，
    /// 下一次重试不早于 <c>PastDueSince + k × (宽限期 / 重试次数)</c>；次数用完就不再自动扣，等宽限期满过期。
    /// <c>Active</c> 不受影响。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 此前没有任何间隔：失败回流释放计费锁、<c>NextBillingTime</c> 不动，下一轮扫描（默认 5 分钟后）立刻再扣，
    /// 默认三次约 10 分钟打满，同一轮的过期扫描随即停服，宽限期从未生效，同一张卡 10 分钟内被连扣三次。
    /// </para>
    /// <para>
    /// 节奏由 <c>PastDueSince</c> 与 <c>RenewalRetryCount</c> 现算，不另存「下次重试时间」：
    /// 数据库里没有可跨提供程序翻译的「列 × 时长」算术，所以把每个重试次数对应的门槛时间算成常量，
    /// 展开成 <c>(RenewalRetryCount == k AND PastDueSince &lt;= now − k × 间隔)</c> 的 OR，并按平衡树组合
    /// （配置值再大也不会生成一条嵌套过深的 SQL）。<c>PastDueSince</c> 为空或计数为 0（经别的路径进的 PastDue）照旧立即可重试。
    /// 用户换卡后的手动重试（<c>RetryBillingAsync</c> / 换卡）不受这个节奏约束。
    /// </para>
    /// </remarks>
    private static Expression<Func<Subscription, bool>> BuildRenewalRetryDuePredicate(DateTime now, SubscriptionOptions options)
    {
        var maxRetry = options.MaxRetryCount;
        var interval = maxRetry > 0
            ? TimeSpan.FromTicks(TimeSpan.FromDays(options.GracePeriodDays).Ticks / maxRetry)
            : TimeSpan.Zero;

        Expression<Func<Subscription, bool>> notPastDue = s => s.Status != SubscriptionStatus.PastDue;
        Expression<Func<Subscription, bool>> firstAttempt = s => s.RenewalRetryCount < maxRetry
            && (s.PastDueSince == null || s.RenewalRetryCount <= 0);

        var terms = new List<Expression<Func<Subscription, bool>>> { notPastDue, firstAttempt };
        for (var failures = 1; failures < maxRetry; failures++)
        {
            var count = failures;
            DateTime? retryNotBefore = now - TimeSpan.FromTicks(interval.Ticks * count);
            terms.Add(s => s.RenewalRetryCount == count && s.PastDueSince <= retryNotBefore);
        }

        var parameter = Expression.Parameter(typeof(Subscription), "s");
        var bodies = terms.Select(t => new ParameterRebinder(t.Parameters[0], parameter).Visit(t.Body)).ToList();
        return Expression.Lambda<Func<Subscription, bool>>(CombineBalanced(bodies, 0, bodies.Count), parameter);

        static Expression CombineBalanced(IReadOnlyList<Expression> items, int from, int to)
        {
            if (to - from == 1)
                return items[from];

            var middle = from + (to - from) / 2;
            return Expression.OrElse(CombineBalanced(items, from, middle), CombineBalanced(items, middle, to));
        }
    }

    /// <summary>把一棵表达式树里的参数换成另一个，用于把多个 lambda 的主体拼到同一个参数上。</summary>
    private sealed class ParameterRebinder(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }

    /// <summary>
    /// 试用转正的应收额：生效计划的现价（条款与订阅对不上时取订阅快照价，见 <see cref="ResolvePlanPrice"/>）
    /// 减去开通试用时快照的折扣（<see cref="Subscription.DiscountAmount"/>），不低于 0。
    /// 折扣是给这次试用的承诺而不是给某个计划的，到期变更换了计划照样从新计划的价格上减；
    /// 与回流侧 <see cref="ResolveAmountFloorAsync"/> 对 TrialConversion 的下界同一公式。
    /// </summary>
    private static decimal ResolveTrialConversionAmount(Subscription subscription, SubscriptionPlan plan)
        => Math.Max(0m, ResolvePlanPrice(subscription, plan) - subscription.DiscountAmount);

    /// <summary>
    /// 这条订阅的下一次扣款是不是一次免费的试用转正（尚未转正的试用，且折扣抵满生效计划的价格）：
    /// 走 <see cref="ChargeTrialConversionAsync"/> 的免费路径，不需要支付方式。
    /// </summary>
    private static bool IsChargeFreeTrialConversion(Subscription subscription, SubscriptionPlan? plan)
        => subscription.TrialEndTime != null
            && subscription.TrialConvertedTime == null
            && plan != null
            && ResolveTrialConversionAmount(subscription, plan) == 0m;

    /// <summary>
    /// 试用转正：应收额大于 0 走 off-session 扣款，回流后转正；折扣抵满全价时直接免费转正 ——
    /// 渠道拒绝 0 元单且不产生任何支付事件，照常去扣会让订阅带着计费锁卡在 Trial 被每轮扫描无限重扫。
    /// </summary>
    private async Task ChargeTrialConversionAsync(Subscription subscription, SubscriptionPlan plan, DateTime now, CancellationToken cancellationToken)
    {
        var amount = ResolveTrialConversionAmount(subscription, plan);
        if (amount > 0)
        {
            await ChargeSubscriptionAsync(subscription, SubscriptionBillingPurpose.TrialConversion, amount, cancellationToken, plan);
            return;
        }

        var tracked = await _subscriptionRepository.FirstOrDefaultAsync(s => s.Id == subscription.Id, cancellationToken);
        if (tracked == null)
            return;

        var statusBefore = tracked.Status;
        MarkTrialConverted(tracked, now, paidAmount: 0m);
        tracked.BillingLockedUntil = null;
        tracked.SuspendedAt = null;
        await _subscriptionRepository.UpdateAsync(tracked, cancellationToken);
        await PublishStandingTransitionAsync(tracked, statusBefore, null, cancellationToken);
        await CancelAwaitingChangesForEndedPeriodAsync(tracked.Id, cancellationToken);

        Logger.LogInformation(
            "Trial converted without a charge: the trial discount covers the full price. SubscriptionNo: {SubscriptionNo}",
            tracked.SubscriptionNo);

        await PublishTrialConvertedAsync(tracked, new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.TrialConversion,
            SubscriptionId = tracked.Id,
            SubscriptionNo = tracked.SubscriptionNo,
            PayerUserId = tracked.UserId,
            PayerAccount = AccountOf(tracked),
            Amount = 0m,
            Currency = tracked.Currency
        });
    }

    /// <summary>
    /// 把订阅从 Trial 推进到付费周期：周期从此刻起算。付款回流与免费转正共用这一段。
    /// 状态按自动续费开关取 Active / PendingRenewal（转正扣款在途时用户关掉了它，这一期照样归他、到期过期）。
    /// </summary>
    private static void MarkTrialConverted(Subscription subscription, DateTime now, decimal paidAmount)
    {
        subscription.TrialConvertedTime = now;
        subscription.Status = ResolveRunningStatus(subscription, now);
        subscription.NextBillingTime = CalculateNextBillingTime(now, subscription.CycleType, subscription.CycleValue);
        subscription.PaidAmount = paidAmount;
        ResetDunning(subscription);
    }

    /// <summary>
    /// 对订阅发起 off-session 扣款（无已保存支付方式则直接降级 PastDue）
    /// </summary>
    /// <remarks>
    /// ★ <b>每一条路径都必须让订阅离开「带着计费锁停在原地」</b>：扫描按 <c>NextBillingTime</c> 升序取前一页，
    /// 一条既不推进也不降级的订阅永远排在队首，这样的行攒满一页，全体续费就此停摆 —— 而扫描照样报「已处理」。
    /// 所以：应收为 0 走免扣款续期；扣款在建单之前就被拒绝（没有支付事件会回流）时在这里走失败分支；
    /// 建单之后的失败由 <c>PaymentFailedEvent</c> 回流收口，这里不再重复计数。
    /// 唯一的例外是扣款结果还没落定：那时订阅<b>刻意</b>带着计费锁停在原地，锁一直持有到那笔支付落定或过期
    /// （见 <see cref="HoldBillingLockUntilSettledAsync"/>），落定的回流会把锁放掉。
    /// </remarks>
    private async Task ChargeSubscriptionAsync(
        Subscription subscription,
        SubscriptionBillingPurpose purpose,
        decimal amount,
        CancellationToken cancellationToken,
        SubscriptionPlan? plan = null)
    {
        // ★ 这条订阅上还有一笔没落定的支付（渠道回报处理中的续费、结果未知的扣款、待付的补差单）：不再发起新的一笔。
        // 换一个流水号就换了幂等键，渠道把两笔都当成新扣款，原来那笔随后入账就是同一期扣了两次。
        // 查不清的时候同样不扣：宁可这一轮不续，也不能在说不准的时候再扣一笔。
        var open = await FindOpenSubscriptionPaymentAsync(subscription, cancellationToken);
        if (!open.Succeeded)
        {
            Logger.LogWarning(
                "Skipping the {Purpose} charge for {SubscriptionNo}: its open payments could not be listed ({Reason}).",
                purpose, subscription.SubscriptionNo, open.Message);
            return;
        }

        if (open.Data != null)
        {
            Logger.LogWarning(
                "Skipping the {Purpose} charge for {SubscriptionNo}: payment {TradeNo} ({Status}) has not settled yet; waiting for it instead of charging again.",
                purpose, subscription.SubscriptionNo, open.Data.TradeNo, open.Data.Status);
            await HoldBillingLockUntilSettledAsync(subscription.Id, open.Data, cancellationToken);
            return;
        }

        // 续费连带收结束的这一期的超额存储费（后付）：计划包含存储时才有，金额并进这一笔扣款，票据上拆成单独一行
        var overage = purpose == SubscriptionBillingPurpose.Renewal
            ? await PriceStorageOverageAsync(subscription, plan ?? subscription.Plan, cancellationToken)
            : null;
        amount += overage?.Amount ?? 0m;

        if (amount <= 0m && purpose == SubscriptionBillingPurpose.Renewal)
        {
            await RenewWithoutChargeAsync(subscription.Id, cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(subscription.PaymentMethodToken))
        {
            await ApplyPaymentFailedAsync(new SubscriptionPaymentContext
            {
                Purpose = purpose,
                SubscriptionId = subscription.Id,
                SubscriptionNo = subscription.SubscriptionNo,
                PayerUserId = subscription.UserId,
                PayerAccount = AccountOf(subscription),
                FailReason = ErrorCodes.SubscriptionPaymentMethodMissing
            }, cancellationToken);
            return;
        }

        // 渠道不可用 / 不支持无人值守扣款（如未开启 vault 的 PayPal、线下渠道）时，
        // ChargeOffSessionAsync 在建单前就返回失败，
        // 不产生任何支付事件，状态机拿不到回流：订阅既不推进也不降级，会被每轮扫描无限重扫。
        // 因此在此显式走失败分支，与"无已保存支付方式"同样降级 PastDue，交宽限期/重试上限收口。
        var provider = _paymentProviderFactory.GetProvider(subscription.ChannelCode);
        if (provider == null || !provider.SupportsOffSessionCharge)
        {
            await ApplyPaymentFailedAsync(new SubscriptionPaymentContext
            {
                Purpose = purpose,
                SubscriptionId = subscription.Id,
                SubscriptionNo = subscription.SubscriptionNo,
                PayerUserId = subscription.UserId,
                PayerAccount = AccountOf(subscription),
                FailReason = ErrorCodes.PaymentOffSessionNotSupported
            }, cancellationToken);
            return;
        }

        if (plan != null && !PlanTermsMatch(subscription, plan))
        {
            // 扣款仍按订阅自己的快照价与币种进行（调用方已按 ResolvePlanPrice 取价），但这条订阅的计划已被改成
            // 另一种计费条款，运营需要知道：调价不再随续费生效，要么把订阅迁到条款一致的计划上，要么恢复计划条款。
            Logger.LogError(
                "Plan {PlanId} now bills {PlanPrice} {PlanCurrency} every {PlanCycleValue} {PlanCycleType}, but subscription {SubscriptionNo} was taken out at {Price} {Currency} every {CycleValue} {CycleType}. "
                + "Charging the subscription's own price; move it to a plan with matching terms.",
                plan.Id, plan.Price, plan.Currency, plan.CycleValue, plan.CycleType,
                subscription.SubscriptionNo, subscription.OriginalPrice, subscription.Currency, subscription.CycleValue, subscription.CycleType);
        }

        // 这一笔付的是从计费日起的一整期（逾期重试付的仍是那一期）
        var periodStart = subscription.NextBillingTime ?? DateTime.UtcNow;
        var meta = new SubscriptionBillingMetadata
        {
            Purpose = purpose,
            SubscriptionId = subscription.Id,
            PeriodStart = periodStart,
            PeriodEnd = CalculateNextBillingTime(periodStart, subscription.CycleType, subscription.CycleValue),
            PlanName = (plan ?? subscription.Plan)?.PlanName,
            StorageOverage = overage
        };

        var charged = await _paymentService.ChargeOffSessionAsync(new OffSessionChargeDto
        {
            BusinessOrderNo = subscription.SubscriptionNo,
            BusinessType = BusinessType.Subscription,
            Amount = amount,
            Currency = subscription.Currency,
            ChannelCode = subscription.ChannelCode,
            Description = $"Subscription {purpose}: {(plan ?? subscription.Plan)?.PlanName}",
            ProviderCustomerId = subscription.ProviderCustomerId,
            PaymentMethodToken = subscription.PaymentMethodToken,
            PaymentMethodBrand = subscription.PaymentMethodBrand,
            PaymentMethodLast4 = subscription.PaymentMethodLast4,
            UserId = subscription.UserId,
            BillingAccount = AccountOf(subscription),
            CustomerName = subscription.CustomerName,
            CustomerEmail = subscription.CustomerEmail,
            ExtraData = meta.ToExtraData()
        }, cancellationToken);

        if (charged.Succeeded)
        {
            // 渠道收下了这笔扣款但还没落定（PayPal capture PENDING、Stripe processing，或结果未知）：
            // 没有任何事件会立刻回流。订阅保持原状、计费锁持有到那笔支付落定或过期，不计重试、不发催款信。
            if (charged.Data is { Status: PaymentStatus.Processing or PaymentStatus.Pending } pending)
            {
                Logger.LogInformation(
                    "Off-session {Purpose} charge {TradeNo} for {SubscriptionNo} has not settled; holding the subscription until it does.",
                    purpose, pending.TradeNo, subscription.SubscriptionNo);
                await HoldBillingLockUntilSettledAsync(subscription.Id, pending, cancellationToken);
            }

            return;
        }

        if (charged.ErrorCode == ErrorCodes.PaymentOffSessionNotAttempted)
        {
            // 建单之前就被拒绝（典型是计税失败）：没有支付记录，也不会有事件回流。
            Logger.LogWarning(
                "Off-session {Purpose} charge for {SubscriptionNo} was rejected before a payment was created: {Reason}",
                purpose, subscription.SubscriptionNo, charged.Message);
            await ApplyPaymentFailedAsync(new SubscriptionPaymentContext
            {
                Purpose = purpose,
                SubscriptionId = subscription.Id,
                SubscriptionNo = subscription.SubscriptionNo,
                PayerUserId = subscription.UserId,
                PayerAccount = AccountOf(subscription),
                FailReason = charged.Message ?? ErrorCodes.PaymentOffSessionChargeFailed
            }, cancellationToken);
            return;
        }

        // 建单之后的失败：PaymentFailedEvent 已经（或将在提交后）把订阅降级，这里只留痕。
        Logger.LogWarning(
            "Off-session {Purpose} charge for {SubscriptionNo} failed; the payment-failed event carries it from here. Reason: {Reason}",
            purpose, subscription.SubscriptionNo, charged.Message);
    }

    /// <summary>
    /// 结束的这一期的超额存储费；不计量、没有超额或算不出来时为 null。
    /// </summary>
    /// <remarks>
    /// ★ 算不出来（存储一侧出错）只记 Error、按没有超额续费，而不是让这一轮续费失败：
    /// 续费扫描对抛异常的订阅只记日志、不推进也不降级，一次存储故障会让这条订阅一直停在原地不续。
    /// </remarks>
    private async Task<StorageOverageCharge?> PriceStorageOverageAsync(Subscription subscription, SubscriptionPlan? plan, CancellationToken cancellationToken)
    {
        if (_storageMeter == null || plan == null)
            return null;

        try
        {
            return await _storageMeter.PriceClosingPeriodAsync(subscription, plan, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Storage overage for {SubscriptionNo} could not be priced; the renewal is charged without it.", subscription.SubscriptionNo);
            return null;
        }
    }

    /// <summary>
    /// 这条订阅名下还没落定（Pending / Processing）、会让这次扣款等它的一笔支付；没有返回成功且 <c>Data = null</c>，查不了返回失败。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 等补差款的变更自己的待付单（<see cref="SubscriptionChange.PaymentTradeNo"/>）不算：补差是对这一期剩余段算的，
    /// 续费结束这一期时它随之作废、待付单被关掉（见 <c>CancelAwaitingChangesForEndedPeriodAsync</c>），续费不必等它。
    /// </para>
    /// <para>
    /// 其余的不分用途（支付行上看不到计费元数据）：在途的续费 / 转正扣款、off-session 补差扣款（它不记待付单号）都让这次扣款等它，
    /// 任何情况下都不会在一笔可能入账的扣款之外再扣一笔。
    /// </para>
    /// </remarks>
    private async Task<Result<PaymentDto?>> FindOpenSubscriptionPaymentAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var endsWithThePeriod = await _changeRepository.AsNoTracking()
            .Where(c => c.SubscriptionId == subscription.Id
                && c.Status == SubscriptionChangeStatus.AwaitingPayment
                && c.PaymentTradeNo != null)
            .Select(c => c.PaymentTradeNo!)
            .ToListAsync(cancellationToken);

        foreach (var openStatus in new[] { PaymentStatus.Processing, PaymentStatus.Pending })
        {
            var open = await _paymentService.GetPaymentListAsync(new PaymentQueryDto
            {
                BusinessOrderNo = subscription.SubscriptionNo,
                BusinessType = BusinessType.Subscription,
                Status = openStatus,
                PageIndex = 1,
                PageSize = OpenPaymentScanSize
            }, cancellationToken: cancellationToken);

            if (!open.Succeeded || open.Data == null)
                return Result.Failure<PaymentDto?>(open.Message ?? ErrorCodes.SubscriptionBillingInProgress, open.Code ?? 409);

            var blocking = open.Data.Items.FirstOrDefault(p => !endsWithThePeriod.Contains(p.TradeNo));
            if (blocking != null)
                return Result.Success<PaymentDto?>(blocking);
        }

        return Result.Success<PaymentDto?>(null);
    }

    /// <summary>
    /// 一次查一条订阅名下多少笔未落定的支付。正常情况下至多一两笔（一笔在途的扣款、一张补差待付单），
    /// 这是上界不是调优项。
    /// </summary>
    private const int OpenPaymentScanSize = 50;

    /// <summary>
    /// 把订阅的计费锁延长到这笔未落定支付的过期时间之后：续费扫描不再选中它（不会再扣一笔），
    /// 取消答 409、过期扫描跳过它（钱到账时订阅还在）。支付落定（完成 / 失败 / 过期）的回流会把锁放掉；
    /// 回流迟迟不来、锁先到期的，下一轮扣款前 <see cref="FindOpenSubscriptionPaymentAsync"/> 会再拦一次。
    /// </summary>
    /// <remarks>
    /// 条件写：只延长仍被持有的锁。回流在另一个请求里已经把锁放掉（支付已经落定）时什么都不做，
    /// 否则一条已经续费成功的订阅会被白白锁上几天。多留出一个计费锁时长，让支付的过期清扫先跑到它。
    /// </remarks>
    private async Task HoldBillingLockUntilSettledAsync(Guid subscriptionId, PaymentDto payment, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var paymentDeadline = payment.ExpireTime ?? payment.CreationTime.AddMinutes(PaymentOptions.AutoCloseExpireMinutes);
        var holdUntil = (paymentDeadline > now ? paymentDeadline : now).AddMinutes(PaymentOptions.BillingLockMinutes);

        await _subscriptionRepository.AsQueryable()
            .Where(s => s.Id == subscriptionId && s.BillingLockedUntil != null && s.BillingLockedUntil < holdUntil)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.BillingLockedUntil, holdUntil), cancellationToken);
    }

    /// <summary>
    /// 应收为 0 的续费：不扣款，直接按付款回流的同一条规则推进一个周期。
    /// </summary>
    /// <remarks>
    /// 0 元计划是合法的（价格只拒负数），而支付侧拒绝 0 元单且不产生任何事件。照常去扣，
    /// 订阅会带着计费锁停在到期那一刻被每轮扫描重扫。与试用折扣抵满全价时的免费转正同一形态。
    /// </remarks>
    private async Task RenewWithoutChargeAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        var tracked = await _subscriptionRepository.FirstOrDefaultAsync(s => s.Id == subscriptionId, cancellationToken);
        if (tracked == null)
            return;

        var now = DateTime.UtcNow;
        var statusBefore = tracked.Status;
        var basis = tracked.NextBillingTime ?? now;
        if (basis < now) basis = now;
        tracked.NextBillingTime = CalculateNextBillingTime(basis, tracked.CycleType, tracked.CycleValue);
        tracked.Status = ResolveRunningStatus(tracked, now);
        tracked.PaidAmount = 0m;
        ResetDunning(tracked);
        tracked.BillingLockedUntil = null;
        tracked.SuspendedAt = null;
        await _subscriptionRepository.UpdateAsync(tracked, cancellationToken);
        await PublishStandingTransitionAsync(tracked, statusBefore, null, cancellationToken);
        await CancelAwaitingChangesForEndedPeriodAsync(tracked.Id, cancellationToken);

        Logger.LogInformation("Renewed without a charge: the plan price is zero. SubscriptionNo: {SubscriptionNo}", tracked.SubscriptionNo);

        await PublishRenewedAsync(tracked, new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Renewal,
            SubscriptionId = tracked.Id,
            SubscriptionNo = tracked.SubscriptionNo,
            PayerUserId = tracked.UserId,
            PayerAccount = AccountOf(tracked),
            Amount = 0m,
            Currency = tracked.Currency
        });
    }

    /// <summary>
    /// 升级补差价收款：有已保存支付方式则 off-session 即时扣款，否则生成待支付订单由用户完成；
    /// 两种路径均在支付完成事件回流后应用计划变更（见 ApplyProrationChangeAsync）。
    /// </summary>
    /// <returns>
    /// 成功时 <c>Data</c> 是回传给前端的待支付单（off-session 当场扣成功时为 null）；
    /// 失败时带着渠道 / 建单的失败原因。★ 此前 off-session 的返回值被直接丢弃：渠道拒付时
    /// 调用方照样答 200，而变更停在等钱的状态里。
    /// </returns>
    private async Task<Result<PaymentOrderResultDto?>> ChargeOrCreateProrationPaymentAsync(
        Subscription subscription, SubscriptionPlan currentPlan, SubscriptionPlan newPlan, Guid changeId, decimal amount, CancellationToken cancellationToken)
    {
        // 补差覆盖从现在到本期结束（计费锚点不变）
        var meta = new SubscriptionBillingMetadata
        {
            Purpose = SubscriptionBillingPurpose.Proration,
            SubscriptionId = subscription.Id,
            ChangeId = changeId,
            PeriodStart = DateTime.UtcNow,
            PeriodEnd = subscription.NextBillingTime,
            PlanName = newPlan.PlanName
        };
        var description = $"Plan change proration: {currentPlan.PlanName} -> {newPlan.PlanName}";

        if (!string.IsNullOrWhiteSpace(subscription.PaymentMethodToken))
        {
            var charged = await _paymentService.ChargeOffSessionAsync(new OffSessionChargeDto
            {
                BusinessOrderNo = subscription.SubscriptionNo,
                BusinessType = BusinessType.Subscription,
                Amount = amount,
                Currency = newPlan.Currency,
                ChannelCode = subscription.ChannelCode,
                Description = description,
                ProviderCustomerId = subscription.ProviderCustomerId,
                PaymentMethodToken = subscription.PaymentMethodToken,
                PaymentMethodBrand = subscription.PaymentMethodBrand,
                PaymentMethodLast4 = subscription.PaymentMethodLast4,
                UserId = subscription.UserId,
                BillingAccount = AccountOf(subscription),
                CustomerName = subscription.CustomerName,
                CustomerEmail = subscription.CustomerEmail,
                ExtraData = meta.ToExtraData()
            }, cancellationToken);

            return charged.Succeeded
                ? Result.Success<PaymentOrderResultDto?>(null)
                : Result.Failure<PaymentOrderResultDto?>(charged.Message ?? ErrorCodes.PaymentOffSessionChargeFailed, charged.Code ?? 400);
        }

        // 未绑卡：生成待支付单并把凭据回传，否则用户拿不到任何可付款的入口。
        // 计费元数据经系统通道写入：用户面的 POST /payments 不接受 BusinessType.Subscription
        var payment = await _paymentService.CreatePaymentAsync(new CreatePaymentDto
        {
            BusinessOrderNo = subscription.SubscriptionNo,
            BusinessType = BusinessType.Subscription,
            Amount = amount,
            Currency = newPlan.Currency,
            ChannelCode = subscription.ChannelCode,
            Description = description,
            ExtraData = meta.ToExtraData(),
            IsSystemInitiated = true
        }, cancellationToken);

        return payment.Succeeded
            ? Result.Success<PaymentOrderResultDto?>(payment.Data)
            : Result.Failure<PaymentOrderResultDto?>(payment.Message ?? ErrorCodes.PaymentCreationFailed, payment.Code ?? 400);
    }

    /// <summary>
    /// 将订阅置为过期并发布过期事件；逾期欠费且配置了 <c>GracePeriodEndAction = Suspend</c> 时改为暂停服务。
    /// </summary>
    private async Task ExpireSubscriptionAsync(Guid subscriptionId, DateTime now, CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository.FirstOrDefaultAsync(s => s.Id == subscriptionId, cancellationToken);
        if (subscription == null
            || subscription.Status == SubscriptionStatus.Expired
            || subscription.Status == SubscriptionStatus.Cancelled
            || subscription.Status == SubscriptionStatus.Suspended)
            return;

        var statusBefore = subscription.Status;

        if (subscription.Status == SubscriptionStatus.PastDue
            && SubscriptionOptions.GracePeriodEndAction == GracePeriodEndAction.Suspend)
        {
            await SuspendSubscriptionAsync(subscription, now, cancellationToken);
            return;
        }

        subscription.Status = SubscriptionStatus.Expired;
        subscription.EndTime = now;
        subscription.BillingLockedUntil = null;
        subscription.NextBillingTime = null;
        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

        // 过期结束的也是一个周期：等补差款的变更随周期一起失效，连待付单一起关（与续费 / 立即取消同形）
        await CancelAwaitingChangesForEndedPeriodAsync(subscription.Id, cancellationToken);

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new SubscriptionExpiredEvent
            {
                SubscriptionId = subscription.Id,
                SubscriptionNo = subscription.SubscriptionNo,
                UserId = subscription.UserId,
                ExpiredTime = now
            });
        }

        Logger.LogInformation("Subscription expired. SubscriptionNo: {SubscriptionNo}", subscription.SubscriptionNo);

        await PublishStandingTransitionAsync(subscription, statusBefore, null, cancellationToken);
    }

    /// <summary>
    /// 宽限期满仍未付清：暂停服务。订阅不结束，照常占着这个产品的名额，等管理者补缴或换卡。
    /// </summary>
    /// <remarks>
    /// <c>NextBillingTime</c> 原样保留（欠着的那一期从它开始），恢复时新的一期从付款那一刻起算。
    /// 等补差款的变更随这一期一起失效（与过期同形）：补差是对本期剩余段算的，而本期已经在欠费里过完了。
    /// </remarks>
    private async Task SuspendSubscriptionAsync(Subscription subscription, DateTime now, CancellationToken cancellationToken)
    {
        var statusBefore = subscription.Status;

        subscription.Status = SubscriptionStatus.Suspended;
        subscription.SuspendedAt = now;
        subscription.BillingLockedUntil = null;
        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

        await CancelAwaitingChangesForEndedPeriodAsync(subscription.Id, cancellationToken);

        Logger.LogWarning("Subscription suspended: still unpaid at the end of the grace period. SubscriptionNo: {SubscriptionNo}, PastDueSince: {PastDueSince}",
            subscription.SubscriptionNo, subscription.PastDueSince);

        await SendSuspensionNoticeAsync(subscription, cancellationToken);
        await PublishStandingTransitionAsync(subscription, statusBefore, null, cancellationToken);
    }

    /// <summary>
    /// 升级补差付款确认后应用计划变更。
    /// </summary>
    /// <remarks>
    /// <para>只接受 <see cref="SubscriptionChangeStatus.AwaitingPayment"/>：补差单只会为等钱的变更而建，
    /// 一条 <c>Pending</c>（到期结算）的变更不该被任何一笔支付提前应用。</para>
    /// <para>★ 不动 <c>NextBillingTime</c>。补差按「剩余比例 × 差价」只覆盖本期剩下的那一段，
    /// 这里若把时钟重置成「现在 + 整周期」，用户付 diff×r 却拿到从现在起完整的一个新周期，
    /// 每次立即升级都少收 新价×(1−r)。锚点留在原处，到期由续费按新价收。</para>
    /// </remarks>
    private async Task ApplyProrationChangeAsync(Subscription subscription, SubscriptionPaymentContext context, DateTime now, CancellationToken cancellationToken)
    {
        if (!context.ChangeId.HasValue)
            return;

        var change = await _changeRepository.FirstOrDefaultAsync(c => c.Id == context.ChangeId.Value, cancellationToken);
        var newPlan = change == null
            ? null
            : await _planRepository.FirstOrDefaultAsync(p => p.Id == change.ToPlanId, cancellationToken);

        // ★ 条件更新（WHERE Status = AwaitingPayment）抢下这条变更，而不是读出来判一次再整行写回：
        // 用户的「取消变更」与周期结束的清理同时在改它，先读后写会让这里把已取消的变更覆盖成 Applied，
        // 新计划就以旧价跑满一个周期（或订阅在新计划上而变更记录写着已取消）。抢不到 = 钱收到了但没有东西可以生效。
        var targetStatus = newPlan == null ? SubscriptionChangeStatus.Cancelled : SubscriptionChangeStatus.Applied;
        if (change == null || !await TryMoveAwaitingChangeAsync(change.Id, targetStatus, cancellationToken))
        {
            // 用户在付款前取消了变更、或支付单过期后又被付掉：钱收到了但没有东西可以生效
            Logger.LogWarning(
                "Proration payment {TradeNo} for subscription {SubscriptionNo} references change {ChangeId} that is no longer awaiting payment (was {Status}); nothing applied, orphan payment may require refund.",
                context.PaymentTradeNo, subscription.SubscriptionNo, context.ChangeId, change?.Status);
            return;
        }

        if (newPlan == null)
        {
            Logger.LogWarning(
                "Proration payment {TradeNo} for subscription {SubscriptionNo}: target plan {PlanId} no longer exists; change cancelled, orphan payment may require refund.",
                context.PaymentTradeNo, subscription.SubscriptionNo, change.ToPlanId);
            return;
        }

        subscription.PlanId = newPlan.Id;
        // 不设 Plan 导航（游离实体会被 EF 当新计划 INSERT）
        subscription.ProductCode = newPlan.ProductCode;
        subscription.CycleType = newPlan.CycleType;
        subscription.CycleValue = newPlan.CycleValue;
        subscription.OriginalPrice = newPlan.Price;
        subscription.Currency = newPlan.Currency;

        // 与到期结算的降级发同一条事件：判据是「一条 SubscriptionChange 变成 Applied」，
        // 功能授权挂在这条上，两条路径必须对称，否则消费方只能收到一半的生效通知。
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new SubscriptionPlanChangeAppliedEvent
            {
                SubscriptionId = subscription.Id,
                SubscriptionNo = subscription.SubscriptionNo,
                UserId = subscription.UserId,
                ChangeId = change.Id,
                FromPlanId = change.FromPlanId,
                ToPlanId = change.ToPlanId,
                ChangeType = change.ChangeType,
                AppliedTime = now
            });
        }
    }

    /// <summary>
    /// 把一条等补差款的变更置为取消（支付失败 / 过期 / 订阅已终止）。条件更新，幂等；返回是否真的取消了它。
    /// </summary>
    private Task<bool> CancelAwaitingChangeAsync(Guid changeId, CancellationToken cancellationToken)
        => TryMoveAwaitingChangeAsync(changeId, SubscriptionChangeStatus.Cancelled, cancellationToken);

    /// <summary>
    /// 把一条仍在等补差款的变更原子地推进到 <paramref name="status"/>（<c>WHERE Status = AwaitingPayment</c>）；返回是否抢到了它。
    /// 补差回流、支付失败、取消变更与周期结束的清理都会改这一行，只有抢到的那一方能继续。
    /// </summary>
    private async Task<bool> TryMoveAwaitingChangeAsync(Guid changeId, SubscriptionChangeStatus status, CancellationToken cancellationToken)
        => await _changeRepository.AsQueryable()
            .Where(c => c.Id == changeId && c.Status == SubscriptionChangeStatus.AwaitingPayment)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status), cancellationToken) > 0;

    /// <summary>
    /// 补差款以「没收到钱」结束（失败 / 作废 / 关单 / 过期）：取消那条还在等钱的变更；只在续费可能正等着这笔款时放掉计费锁。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 此前这里与续费失败共用一次整行写回：把回流开头读出来的订阅连同「计费锁置空」一起写回去。补差单的结束常常发生在
    /// 别的流程当中：续费或试用转正成功、立即取消、过期都会关掉还在等钱的补差单，关单的失败回流在默认的同步事件总线上
    /// 就在关单调用里跑；待付单也可能恰在一次续费扣款的渠道往返期间过期。那一刻计费锁归那次续费所有，放掉它，
    /// 另一个实例就能在续费落地之前对同一期再扣一次；写回的旧快照也会盖住这期间落地的别的写入。
    /// </para>
    /// <para>
    /// 锁只在一种情形下是在等这笔补差款：off-session 补差扣款（变更上不记待付单号）还没落定，续费扫描持锁等它
    /// （见 <c>FindOpenSubscriptionPaymentAsync</c>），它失败了续费才能接着扣。待付单续费从不等（它随周期作废），
    /// 变更已经不在等钱（订阅模块自己先取消了它再关单、或已经应用）的则这次失败不是新消息，两种都不碰锁。
    /// </para>
    /// </remarks>
    private async Task SettleUnpaidProrationAsync(Guid subscriptionId, SubscriptionPaymentContext context, CancellationToken cancellationToken)
    {
        if (!context.ChangeId.HasValue || !await CancelAwaitingChangeAsync(context.ChangeId.Value, cancellationToken))
            return;

        var chargedOffSession = await _changeRepository.AsNoTracking()
            .Where(c => c.Id == context.ChangeId.Value)
            .Select(c => c.PaymentTradeNo == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (chargedOffSession)
            await ReleaseBillingLockAsync(subscriptionId, cancellationToken);
    }

    /// <summary>
    /// 付款必须记在订阅所属的计费账户上。合法路径都满足：首付 / 补差 / 补缴单经系统通道建、显式带订阅的账户，
    /// off-session 扣款同样显式带订阅的账户。
    /// </summary>
    /// <remarks>
    /// ★ 比的是账户不是经办人：单客户部署里经理 A 开通、经理 B 补缴，两笔付款都记在同一个账户上，都该推进这条订阅；
    /// 而另一个账户的人付的钱（哪怕元数据里写着这条订阅）照旧不认。默认的 User 范围下账户就是用户，与按用户比逐字相同。
    /// </remarks>
    private Result VerifyPaymentBelongsToSubscription(Subscription subscription, SubscriptionPaymentContext context)
    {
        var owner = AccountOf(subscription);
        // 没带账户的上下文（升级之前发布、此刻才投递到的支付事件）按付款人的个人账户认：
        // 与回填之后的个人账户订阅对得上，对不上任何非个人账户
        var payer = context.PayerAccount ?? (context.PayerUserId is { } payerUserId ? BillingAccountRef.ForUser(payerUserId) : null);
        if (payer != null && payer == owner)
            return Ok();

        Logger.LogWarning(
            "Ignoring {Purpose} payment {TradeNo} for subscription {SubscriptionNo}: it was paid on account {Payer} (by user {PayerUser}), not the subscription's account {Owner}.",
            context.Purpose, context.PaymentTradeNo, subscription.SubscriptionNo, payer, context.PayerUserId, owner);
        return Fail(ErrorCodes.SubscriptionPaymentOwnerMismatch, 403);
    }

    /// <summary>
    /// 这一次该收的下界（净额，税在其上）。算不出来（变更记录不存在）返回 null，由后续分支自行处理。
    /// </summary>
    private async Task<decimal?> ResolveAmountFloorAsync(Subscription subscription, SubscriptionPaymentContext context, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(context.Currency)
            && !string.Equals(context.Currency, subscription.Currency, StringComparison.OrdinalIgnoreCase))
        {
            // 币种对不上直接把下界抬到不可能满足：99 JPY 不是 99 USD
            return decimal.MaxValue;
        }

        // 计划价可能在订阅存续期间被运营调低：续费按计划的现价扣，下界也得跟着现价走，
        // 否则每一笔按新价扣成的续费都会被这里当成少付而拒绝。取两者中较低的那个，
        // 攻击者两个值都改不了，守卫并不因此变松。
        // 计划的币种或周期已经和订阅对不上时，计划现价是另一种口径下的数字，不能拿来压低下界（扣款侧此时也按快照价收）。
        var plan = await _planRepository.FirstOrDefaultAsync(p => p.Id == subscription.PlanId, cancellationToken);
        var listPrice = Math.Min(subscription.OriginalPrice, plan == null ? subscription.OriginalPrice : ResolvePlanPrice(subscription, plan));

        switch (context.Purpose)
        {
            case SubscriptionBillingPurpose.Initial:
            case SubscriptionBillingPurpose.TrialConversion:
                return listPrice - subscription.DiscountAmount;
            case SubscriptionBillingPurpose.Renewal:
                return listPrice;
            case SubscriptionBillingPurpose.Recovery:
                // 补缴收的就是这一期该收的：未转正的试用按转正价，其余按续费价
                return IsUnconvertedTrial(subscription) ? listPrice - subscription.DiscountAmount : listPrice;
            case SubscriptionBillingPurpose.Proration:
                if (!context.ChangeId.HasValue)
                    return null;
                var change = await _changeRepository.FirstOrDefaultAsync(c => c.Id == context.ChangeId.Value, cancellationToken);
                return change?.ProratedAmount;
            default:
                return null;
        }
    }

    private async Task<Subscription?> LoadForBillingAsync(SubscriptionPaymentContext context, CancellationToken cancellationToken)
    {
        if (context.SubscriptionId is { } id && id != Guid.Empty)
            return await _subscriptionRepository.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (!string.IsNullOrEmpty(context.SubscriptionNo))
            return await _subscriptionRepository.FirstOrDefaultAsync(s => s.SubscriptionNo == context.SubscriptionNo, cancellationToken);
        return null;
    }

    private static void ResetDunning(Subscription subscription)
    {
        subscription.RenewalRetryCount = 0;
        subscription.PastDueSince = null;
    }

    /// <summary>
    /// 催款通知：告诉账单联系人扣款失败、还有多久停服、怎么补救
    /// </summary>
    private async Task SendDunningNoticeAsync(Subscription subscription, string? failReason, CancellationToken cancellationToken)
    {
        if (_notificationService == null)
            return;

        var graceDays = SubscriptionOptions.GracePeriodDays;
        var deadline = (subscription.PastDueSince ?? DateTime.UtcNow).AddDays(graceDays);

        var missingMethod = string.Equals(failReason, ErrorCodes.SubscriptionPaymentMethodMissing, StringComparison.Ordinal);
        var reason = missingMethod ? "no payment method is on file" : "the payment could not be completed";
        var consequence = SubscriptionOptions.GracePeriodEndAction == GracePeriodEndAction.Suspend
            ? "service will be suspended"
            : "the subscription will expire";

        var body =
            $"We could not renew subscription {subscription.SubscriptionNo} because {reason}. " +
            $"Please update your payment method before {deadline:yyyy-MM-dd}, otherwise {consequence}. " +
            $"Attempt {subscription.RenewalRetryCount} of {SubscriptionOptions.MaxRetryCount}.";

        await SendSubscriptionEmailAsync(subscription, new SubscriptionEmail(
            SubscriptionEmailTemplates.PaymentFailed,
            $"Action required: payment failed for {subscription.SubscriptionNo}",
            body,
            new Dictionary<string, object>
            {
                ["SubscriptionNo"] = subscription.SubscriptionNo,
                ["Reason"] = reason,
                ["PaymentMethodMissing"] = missingMethod,
                ["Deadline"] = deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["SuspendsService"] = SubscriptionOptions.GracePeriodEndAction == GracePeriodEndAction.Suspend,
                ["Attempt"] = subscription.RenewalRetryCount,
                ["MaxAttempts"] = SubscriptionOptions.MaxRetryCount,
                ["PaymentMethod"] = DescribePaymentMethod(subscription)
            }), cancellationToken);
    }

    /// <summary>
    /// 暂停服务通知：宽限期满仍未付清，服务已暂停，付清后立即恢复。
    /// </summary>
    private async Task SendSuspensionNoticeAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        if (_notificationService == null)
            return;

        var body =
            $"Subscription {subscription.SubscriptionNo} is still unpaid after the grace period, so service has been suspended. " +
            "Pay the outstanding amount or update the payment method on the subscription page and service resumes immediately.";

        await SendSubscriptionEmailAsync(subscription, new SubscriptionEmail(
            SubscriptionEmailTemplates.Suspended,
            $"Service suspended: {subscription.SubscriptionNo}",
            body,
            new Dictionary<string, object>
            {
                ["SubscriptionNo"] = subscription.SubscriptionNo,
                ["PastDueSince"] = (subscription.PastDueSince ?? DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["PaymentMethod"] = DescribePaymentMethod(subscription)
            }), cancellationToken);
    }

    /// <summary>邮件里提到的卡（「Visa ending 4242」）；没有卡时为空串。</summary>
    private static string DescribePaymentMethod(Subscription subscription)
        => string.IsNullOrWhiteSpace(subscription.PaymentMethodLast4)
            ? string.Empty
            : $"{subscription.PaymentMethodBrand ?? "card"} ending {subscription.PaymentMethodLast4}";

    /// <summary>
    /// 给订阅的账单联系人发一封邮件。缺邮箱或通知模块未加载时安静跳过（计费本身不应因通知失败而中断）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 收件人是<b>账户</b>的账单联系人（<see cref="IBillingAccountService.ResolveContactAsync(BillingAccountRef?, string?, string?, Guid?, CancellationToken)"/>）：账户填了资料就发给资料里的每一个地址，
    /// 否则发给订阅上的快照（开通的经办人），快照也空着（开通时令牌不带邮箱声明）就查开通人账号上的邮箱。单客户部署里经办人可能早已离职，催款信不能只寄给他。
    /// </para>
    /// <para>
    /// ★ 正文走模板（<c>Templates/Notification/Email/{模板名}.cshtml</c>，随本包复制到输出目录，部署方可在模板管理里改写），
    /// 同时带一份纯文本兜底：没加载模板模块、或模板渲染失败时通知模块退回这份文本，而不是发一封空信。
    /// 所有模板变量另带 <c>CustomerName</c> / <c>PlanName</c> / <c>ManageUrl</c>（<c>Payment:Subscription:ManageUrl</c>，可空）。
    /// </para>
    /// </remarks>
    private async Task<bool> SendSubscriptionEmailAsync(Subscription subscription, SubscriptionEmail email, CancellationToken cancellationToken)
    {
        if (_notificationService == null)
            return false;

        var contact = await _billingAccountService.ResolveContactAsync(
            AccountOf(subscription), subscription.CustomerName, subscription.CustomerEmail, subscription.UserId, cancellationToken);
        if (contact.Emails.Count == 0)
            return false;

        var planName = subscription.Plan?.PlanName
            ?? await _planRepository.AsNoTracking().Where(p => p.Id == subscription.PlanId).Select(p => p.PlanName).FirstOrDefaultAsync(cancellationToken);

        var variables = new Dictionary<string, object>(email.Variables)
        {
            ["CustomerName"] = contact.Name ?? string.Empty,
            ["PlanName"] = planName ?? "subscription",
            ["ManageUrl"] = SubscriptionOptions.ManageUrl ?? string.Empty
        };

        var fallback = string.IsNullOrWhiteSpace(SubscriptionOptions.ManageUrl)
            ? email.FallbackBody
            : $"{email.FallbackBody} {SubscriptionOptions.ManageUrl}";

        var result = await _notificationService.CreateAndSendAsync(new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            // 事务性：订阅续费 / 扣款结果，属既有付费关系下必需的往来。
            IsTransactional = true,
            TemplateName = email.TemplateName,
            TemplateVariables = variables,
            Subject = email.Subject,
            Content = $"<p>{WebUtility.HtmlEncode(fallback)}</p>",
            IsHtml = true,
            SendImmediately = true,
            Recipients = contact.Emails
                .Select(address => new RecipientInput { Address = address, Name = contact.Name })
                .ToList()
        }, cancellationToken);

        if (!result.Succeeded)
        {
            Logger.LogWarning("Subscription notification failed. SubscriptionNo: {SubscriptionNo}, Error: {Error}",
                subscription.SubscriptionNo, result.Message);
        }

        return result.Succeeded;
    }

    /// <summary>一封订阅邮件：模板名、主题、模板渲染不出来时的纯文本兜底、模板变量。</summary>
    private sealed record SubscriptionEmail(string TemplateName, string Subject, string FallbackBody, IReadOnlyDictionary<string, object> Variables);

    private async Task PublishRenewedAsync(Subscription subscription, SubscriptionPaymentContext context)
    {
        if (EventBus == null)
            return;

        await EventBus.PublishAsync(new SubscriptionRenewedEvent
        {
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            UserId = subscription.UserId,
            PlanId = subscription.PlanId,
            NewEndTime = subscription.NextBillingTime ?? DateTime.UtcNow,
            Amount = context.Amount,
            Currency = subscription.Currency,
            PaymentTradeNo = context.PaymentTradeNo,
            AutoRenew = subscription.AutoRenew
        });
    }

    private async Task PublishTrialConvertedAsync(Subscription subscription, SubscriptionPaymentContext context)
    {
        if (EventBus == null)
            return;

        await EventBus.PublishAsync(new SubscriptionTrialConvertedEvent
        {
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            UserId = subscription.UserId,
            ConvertedTime = subscription.TrialConvertedTime ?? DateTime.UtcNow,
            PaymentTradeNo = context.PaymentTradeNo
        });
    }
}
