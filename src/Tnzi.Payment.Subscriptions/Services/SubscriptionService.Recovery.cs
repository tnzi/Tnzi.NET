namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// 订阅服务（partial）：账户境况（Good / Warning / Suspended）的变化与事件，以及逾期 / 暂停服务时的当场补缴。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>补缴是有人在场的付款</b>，与后台续费是两条路：续费用已保存的卡无人值守地扣（off-session），卡要求 3-D Secure 时必然失败；
/// 补缴给管理者一张收银台单（PaymentIntent 的 client secret），他在浏览器里用 Stripe Elements 确认、完成验证。
/// 同一张单付款时把卡留存下来（<c>setup_future_usage</c>），回流时换成这条订阅续费用的卡。
/// </para>
/// <para>
/// ★ 补缴单只有一张：再点一次「立即付款」拿回的是同一张单的凭据（<see cref="Subscription.RecoveryPaymentTradeNo"/>），
/// 否则两张同时可付的单都付掉就是同一期收两次。补缴单在途时后台不会再无人值守地扣这条订阅（在途付款检查会拦下）。
/// </para>
/// </remarks>
public partial class SubscriptionService
{
    public async Task<Result<SubscriptionPayNowResultDto>> PayNowAsync(Guid subscriptionId, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        // 与其它用户变更同一个入口：先认账户（要能管理）、再抢计费锁、锁后再读
        var claim = await ClaimForUserChangeAsync(subscriptionId, ownerUserId, cancellationToken);
        if (!claim.Succeeded || claim.Data == null)
            return Fail<SubscriptionPayNowResultDto>(claim.Message ?? ErrorCodes.SubscriptionNotFound, claim.Code ?? 404);

        var subscription = claim.Data;
        try
        {
            if (subscription.Status == SubscriptionStatus.Pending)
                return await ResumeFirstPaymentAsync(subscription, cancellationToken);

            if (subscription.Status is not (SubscriptionStatus.PastDue or SubscriptionStatus.Suspended))
                return Fail<SubscriptionPayNowResultDto>(ErrorCodes.SubscriptionCannotPayNow, 400);

            // 已有一张补缴单还能付：把它的凭据还回去；有一笔别的扣款在途（换卡后的重试、续费）：等它落定
            var open = await FindOpenSubscriptionPaymentAsync(subscription, cancellationToken);
            if (!open.Succeeded)
                return Fail<SubscriptionPayNowResultDto>(ErrorCodes.SubscriptionBillingInProgress, 409);

            if (open.Data != null)
            {
                if (!string.Equals(open.Data.TradeNo, subscription.RecoveryPaymentTradeNo, StringComparison.Ordinal))
                    return Fail<SubscriptionPayNowResultDto>(ErrorCodes.SubscriptionBillingInProgress, 409);

                var existing = await _paymentService.GetPaymentParamsAsync(open.Data.TradeNo, cancellationToken: cancellationToken);
                if (!existing.Succeeded || existing.Data == null)
                    return Fail<SubscriptionPayNowResultDto>(existing.Message ?? ErrorCodes.SubscriptionBillingInProgress, existing.Code ?? 409);

                return Ok(new SubscriptionPayNowResultDto
                {
                    SubscriptionId = subscription.Id,
                    TradeNo = open.Data.TradeNo,
                    PayParams = existing.Data.ClientSecret,
                    Amount = open.Data.PayableAmount,
                    Currency = open.Data.Currency,
                    ExpireTime = open.Data.ExpireTime,
                    ChannelCode = open.Data.ChannelCode
                });
            }

            var now = DateTime.UtcNow;

            // 与续费同一个顺序：到期的待生效变更先结算，再按生效计划的价格收
            var appliedPlan = await ApplyDuePlanChangeForSubscriptionAsync(subscription.Id, now, cancellationToken);
            var billed = await _subscriptionRepository.AsNoTracking()
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.Id == subscription.Id, cancellationToken) ?? subscription;
            var plan = appliedPlan ?? billed.Plan;
            if (plan == null)
                return Fail<SubscriptionPayNowResultDto>(ErrorCodes.SubscriptionPlanNotFound, 404);

            var amount = IsUnconvertedTrial(billed) ? ResolveTrialConversionAmount(billed, plan) : ResolvePlanPrice(billed, plan);

            // 补缴收的是那一次没扣成的续费，它结束的那一期的超额存储费一并收（与续费同一个数）
            var overage = IsUnconvertedTrial(billed) ? null : await PriceStorageOverageAsync(billed, plan, cancellationToken);
            amount += overage?.Amount ?? 0m;

            // 应收为 0（0 元计划、折扣抵满的试用转正）：不建单，按续费 / 转正的免扣款路径直接恢复。两条路径自己放锁
            if (amount <= 0m)
            {
                if (IsUnconvertedTrial(billed))
                    await ChargeTrialConversionAsync(billed, plan, now, cancellationToken);
                else
                    await RenewWithoutChargeAsync(billed.Id, cancellationToken);

                return Ok(new SubscriptionPayNowResultDto
                {
                    SubscriptionId = billed.Id,
                    Amount = 0m,
                    Currency = billed.Currency,
                    ChannelCode = billed.ChannelCode,
                    SettledWithoutPayment = true
                });
            }

            var payment = await _paymentService.CreatePaymentAsync(new CreatePaymentDto
            {
                BusinessOrderNo = billed.SubscriptionNo,
                BusinessType = BusinessType.Subscription,
                Amount = amount,
                Currency = billed.Currency,
                ChannelCode = billed.ChannelCode,
                Description = $"Subscription payment: {plan.PlanName}",
                ExtraData = new SubscriptionBillingMetadata
                {
                    Purpose = SubscriptionBillingPurpose.Recovery,
                    SubscriptionId = billed.Id,
                    // 补缴后新的一期从付款那一刻起算（不追收暂停期间）
                    PeriodStart = now,
                    PeriodEnd = CalculateNextBillingTime(now, billed.CycleType, billed.CycleValue),
                    PlanName = plan.PlanName,
                    StorageOverage = overage
                }.ToExtraData(),
                IsSystemInitiated = true,
                BillingAccount = AccountOf(billed),
                SavePaymentMethod = true
            }, cancellationToken);

            if (!payment.Succeeded || payment.Data == null)
                return Fail<SubscriptionPayNowResultDto>(payment.Message ?? ErrorCodes.PaymentCreationFailed, payment.Code ?? 400);

            var tradeNo = payment.Data.TradeNo;
            await _subscriptionRepository.AsQueryable()
                .Where(s => s.Id == billed.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RecoveryPaymentTradeNo, tradeNo), cancellationToken);

            Logger.LogInformation("Recovery payment {TradeNo} created for {Status} subscription {SubscriptionNo}: {Amount} {Currency}.",
                tradeNo, billed.Status, billed.SubscriptionNo, payment.Data.Amount, payment.Data.Currency);

            return Ok(new SubscriptionPayNowResultDto
            {
                SubscriptionId = billed.Id,
                TradeNo = tradeNo,
                PayParams = payment.Data.PayParams,
                PayUrl = payment.Data.PayUrl,
                Amount = payment.Data.Amount,
                Currency = payment.Data.Currency,
                TaxAmount = payment.Data.TaxAmount,
                ExpireTime = payment.Data.ExpireTime,
                ChannelCode = billed.ChannelCode
            });
        }
        finally
        {
            // 补缴单建好就放锁：付款回流不需要它，而持着它会让管理者在收银台上的这几分钟里换不了计划、取消不了订阅。
            // 后台在这期间不会无人值守地再扣一笔：补缴单是一笔在途付款，续费前的在途检查会等它
            await ReleaseBillingLockAsync(subscription.Id, cancellationToken);
        }
    }

    /// <summary>
    /// 还没付首期的订阅：把那张首期单的凭据还回去，让管理者接着付完。
    /// </summary>
    /// <remarks>
    /// ★ 刻意不新建一张单：首期单是开通时建的，回流按它把订阅激活、把卡留下；再建一张，两张都付掉就是首期收两次。
    /// 首期单已经不能付了（被拒后过期、被关闭）就答 400，不在这里收口那条 Pending：此刻手里持着计费锁，
    /// 而收口要求锁空闲；重新开通时 <see cref="TryEndAbandonedPendingAsync"/> 会收口它。
    /// </remarks>
    private async Task<Result<SubscriptionPayNowResultDto>> ResumeFirstPaymentAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var open = await FindOpenSubscriptionPaymentAsync(subscription, cancellationToken);
        if (!open.Succeeded)
            return Fail<SubscriptionPayNowResultDto>(ErrorCodes.SubscriptionBillingInProgress, 409);

        if (open.Data == null)
            return Fail<SubscriptionPayNowResultDto>(ErrorCodes.SubscriptionCannotPayNow, 400);

        var existing = await _paymentService.GetPaymentParamsAsync(open.Data.TradeNo, cancellationToken: cancellationToken);
        if (!existing.Succeeded || existing.Data == null)
            return Fail<SubscriptionPayNowResultDto>(existing.Message ?? ErrorCodes.SubscriptionBillingInProgress, existing.Code ?? 409);

        return Ok(new SubscriptionPayNowResultDto
        {
            SubscriptionId = subscription.Id,
            TradeNo = open.Data.TradeNo,
            PayParams = existing.Data.ClientSecret,
            Amount = open.Data.PayableAmount,
            Currency = open.Data.Currency,
            TaxAmount = open.Data.TaxAmount,
            ExpireTime = open.Data.ExpireTime,
            ChannelCode = open.Data.ChannelCode
        });
    }

    public async Task<BillingStanding> GetStandingAsync(BillingAccountRef account, string? productCode = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(account);

        var statuses = await _subscriptionRepository.AsNoTracking()
            .Where(s => s.BillingAccountType == account.Type && s.BillingAccountId == account.Id
                && (productCode == null || s.ProductCode == productCode)
                && s.Status != SubscriptionStatus.Cancelled && s.Status != SubscriptionStatus.Expired)
            .Select(s => s.Status)
            .ToListAsync(cancellationToken);

        return BillingStandings.Combine(statuses.Select(BillingStandings.Of));
    }

    public async Task<BillingStandingDto> GetStandingDetailAsync(BillingAccountRef account, string? productCode = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(account);

        var standing = await GetStandingAsync(account, productCode, cancellationToken);
        var dto = new BillingStandingDto { Standing = standing };
        if (standing is not (BillingStanding.Warning or BillingStanding.Suspended))
            return dto;

        // 需要处理的那条：账户境况取的是最需要处理的一种，这里就取那一种状态里最早出问题的一条
        var actionStatus = standing == BillingStanding.Suspended ? SubscriptionStatus.Suspended : SubscriptionStatus.PastDue;
        var subscription = await _subscriptionRepository.AsNoTracking()
            .Include(s => s.Plan)
            .Where(s => s.BillingAccountType == account.Type && s.BillingAccountId == account.Id
                && (productCode == null || s.ProductCode == productCode)
                && s.Status == actionStatus)
            .OrderBy(s => s.PastDueSince)
            .ThenBy(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (subscription == null)
            return dto;

        dto.SubscriptionId = subscription.Id;
        dto.SubscriptionNo = subscription.SubscriptionNo;
        dto.PlanName = subscription.Plan?.PlanName;
        dto.Status = subscription.Status;
        dto.PastDueSince = subscription.PastDueSince;
        dto.SuspendedAt = subscription.SuspendedAt;
        dto.Currency = subscription.Currency;
        if (subscription.Status == SubscriptionStatus.PastDue && subscription.PastDueSince.HasValue)
            dto.GraceDeadline = subscription.PastDueSince.Value.AddDays(SubscriptionOptions.GracePeriodDays);
        if (subscription.Plan != null)
        {
            dto.AmountDue = IsUnconvertedTrial(subscription) ? ResolveTrialConversionAmount(subscription, subscription.Plan) : ResolvePlanPrice(subscription, subscription.Plan);
            if (!IsUnconvertedTrial(subscription) && _storageMeter != null)
            {
                // 只读已有样本：这是一次读取，不在这里当场量
                var overage = await _storageMeter.PriceClosingPeriodAsync(subscription, subscription.Plan, measureWhenUnsampled: false, cancellationToken);
                dto.AmountDue += overage?.Amount ?? 0m;
            }
        }

        return dto;
    }

    /// <summary>
    /// 订阅状态从 <paramref name="from"/> 变到当前状态之后，按境况的变化发事件：进入逾期、暂停服务、恢复正常、首次生效，
    /// 外加一条汇总的 <see cref="BillingAccountStandingChangedEvent"/>。境况没变就什么都不发。
    /// </summary>
    private async Task PublishStandingTransitionAsync(Subscription subscription, SubscriptionStatus from, SubscriptionPaymentContext? context, CancellationToken cancellationToken)
    {
        var to = subscription.Status;
        var previous = BillingStandings.Of(from);
        var current = BillingStandings.Of(to);
        if (previous == current || EventBus == null)
            return;

        var now = DateTime.UtcNow;

        T Stamp<T>(T e) where T : SubscriptionStandingEventBase
        {
            e.SubscriptionId = subscription.Id;
            e.SubscriptionNo = subscription.SubscriptionNo;
            e.BillingAccountType = subscription.BillingAccountType;
            e.BillingAccountId = subscription.BillingAccountId;
            e.ProductCode = subscription.ProductCode;
            e.OccurredTime = now;
            return e;
        }

        switch (current)
        {
            case BillingStanding.Good when from is SubscriptionStatus.PastDue or SubscriptionStatus.Suspended:
                await EventBus.PublishAsync(Stamp(new SubscriptionRecoveredEvent { PreviousStatus = from, PaymentTradeNo = context?.PaymentTradeNo }));
                break;
            case BillingStanding.Good when from == SubscriptionStatus.Pending:
                await EventBus.PublishAsync(Stamp(new SubscriptionActivatedEvent { IsTrial = to == SubscriptionStatus.Trial }));
                break;
            case BillingStanding.Warning:
                await EventBus.PublishAsync(Stamp(new SubscriptionPastDueEvent
                {
                    FailReason = context?.FailReason,
                    GraceDeadline = (subscription.PastDueSince ?? now).AddDays(SubscriptionOptions.GracePeriodDays)
                }));
                break;
            case BillingStanding.Suspended:
                await EventBus.PublishAsync(Stamp(new SubscriptionSuspendedEvent { PastDueSince = subscription.PastDueSince }));
                break;
        }

        await EventBus.PublishAsync(Stamp(new BillingAccountStandingChangedEvent
        {
            PreviousStanding = previous,
            CurrentStanding = current,
            Status = to
        }));
    }
}
