namespace Tnzi.Payment.Services;

/// <summary>
/// 订阅计费引擎（partial）：off-session 扣款、支付完成/失败回流状态机、后台续费/试用转正/过期扫描、PastDue 催款。
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
                    continue;

                // 多实例原子抢占：抢到才处理，避免重复扣款
                if (!await TryClaimAsync(subscription.Id, now, lockUntil, cancellationToken))
                    continue;

                // 发起 off-session 扣款；成功 → 发 PaymentCompletedEvent → 处理器推进周期；
                // 失败/无支付方式 → 降级 PastDue（见 ApplyPaymentFailedAsync）
                await ChargeSubscriptionAsync(subscription, SubscriptionBillingPurpose.Renewal, subscription.Plan.Price, cancellationToken);
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
                    // 试用转正扣款；成功 → 转为 Active；失败 → PastDue
                    await ChargeSubscriptionAsync(subscription, SubscriptionBillingPurpose.TrialConversion, subscription.Plan.Price, cancellationToken);
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
        var subscriptionOptions = PaymentOptions.Subscription;
        var graceLimit = now.AddDays(-subscriptionOptions.GracePeriodDays);
        var maxRetry = subscriptionOptions.MaxRetryCount;

        var overdueIds = await _subscriptionRepository.AsNoTracking()
            .Where(s =>
                // 到期未续费（已关闭自动续费，周期自然结束）
                (s.Status == SubscriptionStatus.PendingRenewal && s.NextBillingTime != null && s.NextBillingTime <= now)
                // 逾期欠费超过宽限期或重试上限
                || (s.Status == SubscriptionStatus.PastDue
                    && (s.PastDueSince == null || s.PastDueSince <= graceLimit || s.RenewalRetryCount >= maxRetry)))
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

        // 终态防复活：取消与在途扣款竞态时，已取消/过期的订阅不应被支付完成"复活"并继续扣款。
        // 仅放过 Initial（订阅尚处 Pending，本就等待首付激活）。
        if (context.Purpose != SubscriptionBillingPurpose.Initial
            && (subscription.Status == SubscriptionStatus.Cancelled || subscription.Status == SubscriptionStatus.Expired))
        {
            Logger.LogWarning(
                "Ignoring {Purpose} payment for non-active subscription {SubscriptionNo} (status={Status}); orphan payment {TradeNo} may require refund.",
                context.Purpose, subscription.SubscriptionNo, subscription.Status, context.PaymentTradeNo);

            if (context.Purpose == SubscriptionBillingPurpose.Proration && context.ChangeId.HasValue)
            {
                var pendingChange = await _changeRepository.FirstOrDefaultAsync(c => c.Id == context.ChangeId.Value, cancellationToken);
                if (pendingChange is { Status: SubscriptionChangeStatus.Pending })
                {
                    pendingChange.Status = SubscriptionChangeStatus.Cancelled;
                    await _changeRepository.UpdateAsync(pendingChange, cancellationToken);
                }
            }

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
                await PublishRenewedAsync(subscription, context);
                break;
            }

            case SubscriptionBillingPurpose.TrialConversion:
                subscription.Status = SubscriptionStatus.Active;
                subscription.TrialConvertedTime = now;
                subscription.NextBillingTime = CalculateNextBillingTime(now, subscription.CycleType, subscription.CycleValue);
                subscription.PaidAmount = context.Amount;
                ResetDunning(subscription);
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
        return Ok();
    }

    public async Task<Result> ApplyPaymentFailedAsync(SubscriptionPaymentContext context, CancellationToken cancellationToken = default)
    {
        Check.NotNull(context);

        var subscription = await LoadForBillingAsync(context, cancellationToken);
        if (subscription == null)
            return Fail(ErrorCodes.SubscriptionNotFound, 404);

        var now = DateTime.UtcNow;

        switch (context.Purpose)
        {
            case SubscriptionBillingPurpose.Renewal:
            case SubscriptionBillingPurpose.TrialConversion:
                subscription.RenewalRetryCount++;
                subscription.PastDueSince ??= now;
                subscription.Status = SubscriptionStatus.PastDue;
                Logger.LogWarning(
                    "Subscription billing failed -> PastDue. SubscriptionNo: {SubscriptionNo}, Retry: {Retry}, Reason: {Reason}",
                    subscription.SubscriptionNo, subscription.RenewalRetryCount, context.FailReason);
                break;

            case SubscriptionBillingPurpose.Proration:
                if (context.ChangeId.HasValue)
                {
                    var change = await _changeRepository.FirstOrDefaultAsync(c => c.Id == context.ChangeId.Value, cancellationToken);
                    if (change is { Status: SubscriptionChangeStatus.Pending })
                    {
                        change.Status = SubscriptionChangeStatus.Cancelled;
                        await _changeRepository.UpdateAsync(change, cancellationToken);
                    }
                }
                break;

            case SubscriptionBillingPurpose.Initial:
                // 首次开通付款失败：保持 Pending，允许用户重试
                break;
        }

        subscription.BillingLockedUntil = null;
        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);
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
    /// 对订阅发起 off-session 扣款（无已保存支付方式则直接降级 PastDue）
    /// </summary>
    private async Task ChargeSubscriptionAsync(Subscription subscription, SubscriptionBillingPurpose purpose, decimal amount, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subscription.PaymentMethodToken))
        {
            await ApplyPaymentFailedAsync(new SubscriptionPaymentContext
            {
                Purpose = purpose,
                SubscriptionId = subscription.Id,
                SubscriptionNo = subscription.SubscriptionNo,
                FailReason = ErrorCodes.SubscriptionPaymentMethodMissing
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
            Description = $"Subscription {purpose}: {subscription.Plan?.PlanName}",
            ProviderCustomerId = subscription.ProviderCustomerId,
            PaymentMethodToken = subscription.PaymentMethodToken,
            ExtraData = meta.ToExtraData()
        }, cancellationToken);
    }

    /// <summary>
    /// 升级补差价收款：有已保存支付方式则 off-session 即时扣款，否则生成待支付订单由用户完成；
    /// 两种路径均在支付完成事件回流后应用计划变更（见 ApplyProrationChangeAsync）
    /// </summary>
    private async Task ChargeOrCreateProrationPaymentAsync(
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
            await _paymentService.ChargeOffSessionAsync(new OffSessionChargeDto
            {
                BusinessOrderNo = subscription.SubscriptionNo,
                BusinessType = BusinessType.Subscription,
                Amount = amount,
                Currency = newPlan.Currency,
                ChannelCode = subscription.ChannelCode,
                Description = description,
                ProviderCustomerId = subscription.ProviderCustomerId,
                PaymentMethodToken = subscription.PaymentMethodToken,
                ExtraData = meta.ToExtraData()
            }, cancellationToken);
        }
        else
        {
            await _paymentService.CreatePaymentAsync(new CreatePaymentDto
            {
                BusinessOrderNo = subscription.SubscriptionNo,
                BusinessType = BusinessType.Subscription,
                Amount = amount,
                Currency = newPlan.Currency,
                ChannelCode = subscription.ChannelCode,
                Description = description,
                ExtraData = meta.ToExtraData()
            }, cancellationToken);
        }
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
        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

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
    /// 升级补差付款确认后应用计划变更
    /// </summary>
    private async Task ApplyProrationChangeAsync(Subscription subscription, SubscriptionPaymentContext context, DateTime now, CancellationToken cancellationToken)
    {
        if (!context.ChangeId.HasValue)
            return;

        var change = await _changeRepository.FirstOrDefaultAsync(c => c.Id == context.ChangeId.Value, cancellationToken);
        if (change is not { Status: SubscriptionChangeStatus.Pending })
            return;

        var newPlan = await _planRepository.FirstOrDefaultAsync(p => p.Id == change.ToPlanId, cancellationToken);
        if (newPlan == null)
            return;

        subscription.PlanId = newPlan.Id;
        subscription.Plan = newPlan;
        subscription.CycleType = newPlan.CycleType;
        subscription.CycleValue = newPlan.CycleValue;
        subscription.OriginalPrice = newPlan.Price;
        subscription.Currency = newPlan.Currency;
        subscription.NextBillingTime = CalculateNextBillingTime(now, newPlan.CycleType, newPlan.CycleValue);

        change.Status = SubscriptionChangeStatus.Applied;
        await _changeRepository.UpdateAsync(change, cancellationToken);
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
