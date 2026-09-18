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

        // 到期且自动续费的订阅（含上轮失败降级 PastDue 的重试），分页 + 锁过滤
        var dueSubscriptions = await _subscriptionRepository.AsNoTracking()
            .Where(s => s.AutoRenew
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue)
                && s.NextBillingTime != null
                && s.NextBillingTime <= now
                && (s.BillingLockedUntil == null || s.BillingLockedUntil < now))
            .Include(s => s.Plan)
            .OrderBy(s => s.NextBillingTime)
            .Take(BillingScanPageSize)
            .ToListAsync(cancellationToken);

        if (dueSubscriptions.Count == 0)
            return Ok(0);

        var processed = 0;
        foreach (var subscription in dueSubscriptions)
        {
            try
            {
                if (subscription.Plan == null)
                {
                    // 静默跳过等于这条订阅从此再也不被续费，而没有任何迹象。
                    // 计划被硬删或数据迁移出错都会走到这里，两者都要有人知道。
                    Logger.LogError(
                        "Skipping renewal for {SubscriptionNo}: plan {PlanId} could not be loaded. "
                        + "This subscription will never renew until the plan exists again.",
                        subscription.SubscriptionNo, subscription.PlanId);
                    continue;
                }

                // 多实例原子抢占：抢到才处理，避免重复扣款
                if (!await TryClaimAsync(subscription.Id, now, lockUntil, cancellationToken))
                    continue;

                // ★ 先结算到期的待生效变更，再扣款。降级约定的就是「本周期末生效」，
                // 而本周期末正是这一刻 —— 顺序反过来，用户在自己已经降级的那一期
                // 仍被按旧价收钱，而订阅详情页此后显示的是新计划（账单与页面对不上，
                // 且没有任何一条日志或状态能看出发生过这件事）。
                var effectivePlan = await ApplyDuePlanChangeForSubscriptionAsync(subscription.Id, now, cancellationToken)
                    ?? subscription.Plan;

                // 发起 off-session 扣款；成功 → 发 PaymentCompletedEvent → 处理器推进周期；
                // 失败/无支付方式 → 降级 PastDue（见 ApplyPaymentFailedAsync）
                await ChargeSubscriptionAsync(
                    subscription, SubscriptionBillingPurpose.Renewal, effectivePlan.Price, cancellationToken, effectivePlan);
                processed++;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Subscription renewal failed. SubscriptionNo: {SubscriptionNo}", subscription.SubscriptionNo);
            }
        }

        Logger.LogInformation("Processed renewal for {Count} due subscriptions", processed);
        return Ok(processed);
    }

    public async Task<Result<int>> ConvertDueTrialsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var lockUntil = now.AddMinutes(PaymentOptions.BillingLockMinutes);

        var dueTrials = await _subscriptionRepository.AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Trial
                && s.TrialEndTime != null
                && s.TrialEndTime <= now
                && (s.BillingLockedUntil == null || s.BillingLockedUntil < now))
            .Include(s => s.Plan)
            .OrderBy(s => s.TrialEndTime)
            .Take(BillingScanPageSize)
            .ToListAsync(cancellationToken);

        if (dueTrials.Count == 0)
            return Ok(0);

        var processed = 0;
        foreach (var subscription in dueTrials)
        {
            try
            {
                if (!await TryClaimAsync(subscription.Id, now, lockUntil, cancellationToken))
                    continue;

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
                    var effectivePlan = await ApplyDuePlanChangeForSubscriptionAsync(subscription.Id, now, cancellationToken)
                        ?? subscription.Plan;

                    // 试用转正扣款（计划现价 − 试用折扣）；成功 → 转为 Active；失败 → PastDue
                    await ChargeTrialConversionAsync(subscription, effectivePlan, now, cancellationToken);
                }

                processed++;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Trial conversion failed. SubscriptionNo: {SubscriptionNo}", subscription.SubscriptionNo);
            }
        }

        Logger.LogInformation("Processed trial conversion for {Count} subscriptions", processed);
        return Ok(processed);
    }

    public async Task<Result<int>> ExpireOverdueSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var subscriptionOptions = SubscriptionOptions;
        var graceLimit = now.AddDays(-subscriptionOptions.GracePeriodDays);
        var maxRetry = subscriptionOptions.MaxRetryCount;

        var overdueIds = await _subscriptionRepository.AsNoTracking()
            .Where(s =>
                // 到期未续费（已关闭自动续费，周期自然结束）
                (s.Status == SubscriptionStatus.PendingRenewal && s.NextBillingTime != null && s.NextBillingTime <= now)
                // 逾期欠费超过宽限期或重试上限。
                // ★ PastDueSince 为 null 时**不**立刻过期：那一列由 ApplyPaymentFailedAsync 写入，
                // 为 null 说明这条订阅是经别的路径进的 PastDue（数据迁移、人工改状态），
                // 「不知道欠了多久」不能读成「欠了很久」—— 那个方向是把宽限期直接跳过，
                // 用户毫无预警地被停服。交给重试上限收口，重试计数同样为 0 时就等下一次扣款失败写上它。
                || (s.Status == SubscriptionStatus.PastDue
                    && ((s.PastDueSince != null && s.PastDueSince <= graceLimit) || s.RenewalRetryCount >= maxRetry)))
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

    public async Task<Result<int>> ResumeDuePausedSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var duePaused = await _subscriptionRepository
            .Where(s => s.Status == SubscriptionStatus.Paused
                && s.PausedUntil != null
                && s.PausedUntil <= now)
            .OrderBy(s => s.PausedUntil)
            .Take(BillingScanPageSize)
            .ToListAsync(cancellationToken);

        if (duePaused.Count == 0)
            return Ok(0);

        var resumed = 0;
        foreach (var subscription in duePaused)
        {
            try
            {
                await ResumeInternalAsync(subscription, cancellationToken);
                resumed++;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Subscription auto-resume failed. SubscriptionNo: {SubscriptionNo}", subscription.SubscriptionNo);
            }
        }

        Logger.LogInformation("Auto-resumed {Count} paused subscriptions", resumed);
        return Ok(resumed);
    }

    public async Task<Result<int>> SendRenewalRemindersAsync(CancellationToken cancellationToken = default)
    {
        var reminderDays = SubscriptionOptions.AutoRenewalReminderDays;
        if (reminderDays <= 0 || _notificationService == null)
            return Ok(0);

        var now = DateTime.UtcNow;
        var threshold = now.AddDays(reminderDays);

        // 只提醒本周期尚未提醒过的订阅：以 NextBillingTime 作为"周期标识"，
        // 续费成功后计费时间前移，下一周期自然重新具备提醒资格。
        var candidates = await _subscriptionRepository
            .Where(s => s.AutoRenew
                && s.Status == SubscriptionStatus.Active
                && s.NextBillingTime != null
                && s.NextBillingTime > now
                && s.NextBillingTime <= threshold
                && s.CustomerEmail != null
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
                var hasPaymentMethod = !string.IsNullOrWhiteSpace(subscription.PaymentMethodToken);
                var body = hasPaymentMethod
                    ? $"Your {subscription.Plan?.PlanName ?? "subscription"} renews on {subscription.NextBillingTime:yyyy-MM-dd} for {subscription.OriginalPrice} {subscription.Currency}."
                    : $"Your {subscription.Plan?.PlanName ?? "subscription"} renews on {subscription.NextBillingTime:yyyy-MM-dd}, but no payment method is on file. Please add one to avoid interruption.";

                var delivered = await SendSubscriptionEmailAsync(
                    subscription,
                    $"Upcoming renewal for {subscription.SubscriptionNo}",
                    body,
                    cancellationToken);

                if (!delivered)
                    continue;

                // 标记的是"针对哪个周期发过"，不是"发过没有"，才能保证每个周期恰好提醒一次
                subscription.RenewalReminderSentFor = subscription.NextBillingTime;
                await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);
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

    public async Task<Result> ApplyPaymentCompletedAsync(SubscriptionPaymentContext context, CancellationToken cancellationToken = default)
    {
        Check.NotNull(context);

        var subscription = await LoadForBillingAsync(context, cancellationToken);
        if (subscription == null)
            return Fail(ErrorCodes.SubscriptionNotFound, 404);

        var now = DateTime.UtcNow;

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
        // 仅放过 Initial（订阅尚处 Pending，本就等待首付激活）。
        if (context.Purpose != SubscriptionBillingPurpose.Initial
            && (subscription.Status == SubscriptionStatus.Cancelled || subscription.Status == SubscriptionStatus.Expired))
        {
            Logger.LogWarning(
                "Ignoring {Purpose} payment for non-active subscription {SubscriptionNo} (status={Status}); orphan payment {TradeNo} may require refund.",
                context.Purpose, subscription.SubscriptionNo, subscription.Status, context.PaymentTradeNo);

            if (context.Purpose == SubscriptionBillingPurpose.Proration && context.ChangeId.HasValue)
                await CancelAwaitingChangeAsync(context.ChangeId.Value, cancellationToken);

            subscription.BillingLockedUntil = null;
            await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);
            return Ok();
        }

        switch (context.Purpose)
        {
            case SubscriptionBillingPurpose.Initial:
                if (subscription.Status == SubscriptionStatus.Pending)
                {
                    subscription.Status = SubscriptionStatus.Active;
                    if (subscription.StartTime == default)
                        subscription.StartTime = now;
                    subscription.NextBillingTime ??= CalculateNextBillingTime(now, subscription.CycleType, subscription.CycleValue);
                    subscription.PaidAmount = context.Amount;
                    ResetDunning(subscription);
                }
                break;

            case SubscriptionBillingPurpose.Renewal:
            {
                var basis = subscription.NextBillingTime ?? now;
                if (basis < now) basis = now; // 逾期续费从当前时间起算
                subscription.NextBillingTime = CalculateNextBillingTime(basis, subscription.CycleType, subscription.CycleValue);
                subscription.Status = SubscriptionStatus.Active;
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

        if (!string.IsNullOrEmpty(context.PaymentTradeNo))
            subscription.LastBillingTradeNo = context.PaymentTradeNo;
        subscription.BillingLockedUntil = null;
        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

        // 扣款成功回写支付方式的最近使用时间，便于识别长期未用/已失效的卡
        if (subscription.StoredPaymentMethodId.HasValue)
            await _paymentMethodService.MarkUsedAsync(subscription.StoredPaymentMethodId.Value, cancellationToken);

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
                if (context.ChangeId.HasValue)
                    await CancelAwaitingChangeAsync(context.ChangeId.Value, cancellationToken);
                break;

            case SubscriptionBillingPurpose.Initial:
                // 首次开通付款失败：保持 Pending，允许用户重试
                break;
        }

        subscription.BillingLockedUntil = null;
        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

        // 催款：只改状态不通知，用户根本不知道自己被停服了
        if (shouldNotify)
            await SendDunningNoticeAsync(subscription, context.FailReason, cancellationToken);

        return Ok();
    }

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
    /// 用户换卡后主动重试一次扣款（不等下一轮扫描）
    /// </summary>
    private async Task RetryBillingInternalAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var plan = subscription.Plan
            ?? await _planRepository.FirstOrDefaultAsync(p => p.Id == subscription.PlanId, cancellationToken);

        if (plan == null)
            return;

        var now = DateTime.UtcNow;
        if (!await TryClaimAsync(subscription.Id, now, now.AddMinutes(PaymentOptions.BillingLockMinutes), cancellationToken))
            return;

        // 计划显式传入而不是挂到导航属性上：订阅实体随后可能被保存，
        // 挂一个游离的计划会让 EF 把它当新计划插入
        if (subscription.TrialEndTime != null && subscription.TrialConvertedTime == null)
            await ChargeTrialConversionAsync(subscription, plan, now, cancellationToken);
        else
            await ChargeSubscriptionAsync(subscription, SubscriptionBillingPurpose.Renewal, plan.Price, cancellationToken, plan);
    }

    /// <summary>
    /// 试用转正的应收额：生效计划的现价减去开通试用时快照的折扣（<see cref="Subscription.DiscountAmount"/>），不低于 0。
    /// 折扣是给这次试用的承诺而不是给某个计划的，到期变更换了计划照样从新计划的价格上减；
    /// 与回流侧 <see cref="ResolveAmountFloorAsync"/> 对 TrialConversion 的下界同一公式。
    /// </summary>
    private static decimal ResolveTrialConversionAmount(Subscription subscription, SubscriptionPlan plan)
        => Math.Max(0m, plan.Price - subscription.DiscountAmount);

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

        MarkTrialConverted(tracked, now, paidAmount: 0m);
        tracked.BillingLockedUntil = null;
        await _subscriptionRepository.UpdateAsync(tracked, cancellationToken);
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
            Amount = 0m,
            Currency = tracked.Currency
        });
    }

    /// <summary>
    /// 把订阅从 Trial 推进到 Active：付费周期从此刻起算。付款回流与免费转正共用这一段。
    /// </summary>
    private static void MarkTrialConverted(Subscription subscription, DateTime now, decimal paidAmount)
    {
        subscription.Status = SubscriptionStatus.Active;
        subscription.TrialConvertedTime = now;
        subscription.NextBillingTime = CalculateNextBillingTime(now, subscription.CycleType, subscription.CycleValue);
        subscription.PaidAmount = paidAmount;
        ResetDunning(subscription);
    }

    /// <summary>
    /// 对订阅发起 off-session 扣款（无已保存支付方式则直接降级 PastDue）
    /// </summary>
    private async Task ChargeSubscriptionAsync(
        Subscription subscription,
        SubscriptionBillingPurpose purpose,
        decimal amount,
        CancellationToken cancellationToken,
        SubscriptionPlan? plan = null)
    {
        if (string.IsNullOrWhiteSpace(subscription.PaymentMethodToken))
        {
            await ApplyPaymentFailedAsync(new SubscriptionPaymentContext
            {
                Purpose = purpose,
                SubscriptionId = subscription.Id,
                SubscriptionNo = subscription.SubscriptionNo,
                PayerUserId = subscription.UserId,
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
                FailReason = ErrorCodes.PaymentOffSessionNotSupported
            }, cancellationToken);
            return;
        }

        var meta = new SubscriptionBillingMetadata
        {
            Purpose = purpose,
            SubscriptionId = subscription.Id
        };

        await _paymentService.ChargeOffSessionAsync(new OffSessionChargeDto
        {
            BusinessOrderNo = subscription.SubscriptionNo,
            BusinessType = BusinessType.Subscription,
            Amount = amount,
            Currency = subscription.Currency,
            ChannelCode = subscription.ChannelCode,
            Description = $"Subscription {purpose}: {(plan ?? subscription.Plan)?.PlanName}",
            ProviderCustomerId = subscription.ProviderCustomerId,
            PaymentMethodToken = subscription.PaymentMethodToken,
            UserId = subscription.UserId,
            CustomerName = subscription.CustomerName,
            CustomerEmail = subscription.CustomerEmail,
            ExtraData = meta.ToExtraData()
        }, cancellationToken);
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
        var meta = new SubscriptionBillingMetadata
        {
            Purpose = SubscriptionBillingPurpose.Proration,
            SubscriptionId = subscription.Id,
            ChangeId = changeId
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
                UserId = subscription.UserId,
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
    /// 将订阅置为过期并发布过期事件
    /// </summary>
    private async Task ExpireSubscriptionAsync(Guid subscriptionId, DateTime now, CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository.FirstOrDefaultAsync(s => s.Id == subscriptionId, cancellationToken);
        if (subscription == null
            || subscription.Status == SubscriptionStatus.Expired
            || subscription.Status == SubscriptionStatus.Cancelled)
            return;

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
        if (change is not { Status: SubscriptionChangeStatus.AwaitingPayment })
        {
            // 用户在付款前取消了变更、或支付单过期后又被付掉：钱收到了但没有东西可以生效
            Logger.LogWarning(
                "Proration payment {TradeNo} for subscription {SubscriptionNo} references change {ChangeId} in status {Status}; nothing applied, orphan payment may require refund.",
                context.PaymentTradeNo, subscription.SubscriptionNo, context.ChangeId, change?.Status);
            return;
        }

        var newPlan = await _planRepository.FirstOrDefaultAsync(p => p.Id == change.ToPlanId, cancellationToken);
        if (newPlan == null)
        {
            change.Status = SubscriptionChangeStatus.Cancelled;
            await _changeRepository.UpdateAsync(change, cancellationToken);
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

        change.Status = SubscriptionChangeStatus.Applied;
        await _changeRepository.UpdateAsync(change, cancellationToken);

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
    /// 把一条等补差款的变更置为取消（支付失败 / 过期 / 订阅已终止）。条件更新，幂等。
    /// </summary>
    private Task CancelAwaitingChangeAsync(Guid changeId, CancellationToken cancellationToken)
        => _changeRepository.AsQueryable()
            .Where(c => c.Id == changeId && c.Status == SubscriptionChangeStatus.AwaitingPayment)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SubscriptionChangeStatus.Cancelled), cancellationToken);

    /// <summary>
    /// 付款人必须就是订阅主。三条合法路径的付款人都等于订阅主：首付与补差单在本人请求内建
    /// （<c>Payment.UserId = CurrentUser.Id</c>），off-session 显式传 <c>subscription.UserId</c>。
    /// </summary>
    private Result VerifyPaymentBelongsToSubscription(Subscription subscription, SubscriptionPaymentContext context)
    {
        if (context.PayerUserId == subscription.UserId)
            return Ok();

        Logger.LogWarning(
            "Ignoring {Purpose} payment {TradeNo} for subscription {SubscriptionNo}: payer {Payer} is not the subscriber {Owner}.",
            context.Purpose, context.PaymentTradeNo, subscription.SubscriptionNo, context.PayerUserId, subscription.UserId);
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
        var plan = await _planRepository.FirstOrDefaultAsync(p => p.Id == subscription.PlanId, cancellationToken);
        var listPrice = Math.Min(subscription.OriginalPrice, plan?.Price ?? subscription.OriginalPrice);

        switch (context.Purpose)
        {
            case SubscriptionBillingPurpose.Initial:
            case SubscriptionBillingPurpose.TrialConversion:
                return listPrice - subscription.DiscountAmount;
            case SubscriptionBillingPurpose.Renewal:
                return listPrice;
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
    /// 催款通知：告诉用户扣款失败、还有多久停服、怎么补救
    /// </summary>
    private async Task SendDunningNoticeAsync(Subscription subscription, string? failReason, CancellationToken cancellationToken)
    {
        if (_notificationService == null)
            return;

        var graceDays = SubscriptionOptions.GracePeriodDays;
        var deadline = (subscription.PastDueSince ?? DateTime.UtcNow).AddDays(graceDays);

        var reason = string.Equals(failReason, ErrorCodes.SubscriptionPaymentMethodMissing, StringComparison.Ordinal)
            ? "no payment method is on file"
            : "the payment could not be completed";

        var body =
            $"We could not renew subscription {subscription.SubscriptionNo} because {reason}. " +
            $"Please update your payment method before {deadline:yyyy-MM-dd} to keep your service active. " +
            $"Attempt {subscription.RenewalRetryCount} of {SubscriptionOptions.MaxRetryCount}.";

        await SendSubscriptionEmailAsync(
            subscription,
            $"Action required: payment failed for {subscription.SubscriptionNo}",
            body,
            cancellationToken);
    }

    /// <summary>
    /// 给订阅的账单联系人发一封邮件。缺邮箱或通知模块未加载时安静跳过（计费本身不应因通知失败而中断）。
    /// </summary>
    private async Task<bool> SendSubscriptionEmailAsync(Subscription subscription, string subject, string body, CancellationToken cancellationToken)
    {
        if (_notificationService == null || string.IsNullOrWhiteSpace(subscription.CustomerEmail))
            return false;

        var result = await _notificationService.CreateAndSendAsync(new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            // 事务性：订阅续费 / 扣款结果，属既有付费关系下必需的往来。
            IsTransactional = true,
            Subject = subject,
            Content = body,
            IsHtml = false,
            SendImmediately = true,
            Recipients =
            [
                new RecipientInput
                {
                    Address = subscription.CustomerEmail,
                    Name = subscription.CustomerName
                }
            ]
        }, cancellationToken);

        if (!result.Succeeded)
        {
            Logger.LogWarning("Subscription notification failed. SubscriptionNo: {SubscriptionNo}, Error: {Error}",
                subscription.SubscriptionNo, result.Message);
        }

        return result.Succeeded;
    }

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
