namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// 订阅服务（partial）：计划变更（升降级）与按比例计费。
/// </summary>
/// <remarks>
/// <para>
/// 变更与预览共用同一套前置校验（<see cref="PlanChangeContext"/>），两条路径的规则因此不可能漂移。
/// 升级立即生效需先收补差款，收款确认后才应用计划（见 SubscriptionService.Billing.cs 的 ApplyProrationChangeAsync）；
/// 降级一律等到周期结束，避免用户在已付费周期内被降权。
/// </para>
/// <para>
/// ★ <b>计费模型：立即升级保留计费锚点。</b>补差公式是「(新价 − 旧价) × 本期剩余比例」，
/// 它只覆盖本期剩下的那一段，所以升级之后 <c>NextBillingTime</c> 原地不动，到期由续费按新价扣款。
/// 此前这里与付费回流都把时钟重置成「现在 + 整周期」：用户付 diff×r 却拿到从现在起完整的一个新周期，
/// 每次立即升级都少收 新价×(1−r)；周期末尾 r→0 时补差四舍五入到 0，更是免费升级外加跳过一次续费扣款。
/// 若产品要的是 Stripe 式「重置周期」，公式必须同时改成「新价全额 − 旧价×r」，两者只能取一。
/// </para>
/// <para>
/// ★ <b>等钱的变更不是到期变更。</b>立即升级需要收款时，变更写成 <see cref="SubscriptionChangeStatus.AwaitingPayment"/>
/// 而不是 <c>Pending</c>：到期结算的两条扫描只取 <c>Pending</c>，否则一条 <c>EffectiveDate = now</c>
/// 的记录会在补差款到账之前就被当成「到期变更」免费应用。
/// </para>
/// </remarks>
public partial class SubscriptionService
{
    public async Task<Result<SubscriptionChangeDto>> ChangeSubscriptionPlanAsync(Guid subscriptionId, ChangeSubscriptionPlanDto input, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var context = await LoadChangeContextAsync(subscriptionId, input.NewPlanId, ownerUserId, cancellationToken);
        if (!context.Succeeded || context.Data == null)
            return Fail<SubscriptionChangeDto>(context.Message ?? ErrorCodes.SubscriptionNotFound, context.Code ?? 400);

        var (subscription, currentPlan, newPlan) = (context.Data.Subscription, context.Data.CurrentPlan, context.Data.NewPlan);

        // 检查是否有待生效 / 等补差款的变更
        var pendingChange = await _changeRepository.FirstOrDefaultAsync(
            c => c.SubscriptionId == subscriptionId
                && (c.Status == SubscriptionChangeStatus.Pending || c.Status == SubscriptionChangeStatus.AwaitingPayment),
            cancellationToken);
        if (pendingChange != null)
            return Fail<SubscriptionChangeDto>(ErrorCodes.SubscriptionChangePending, 400);

        // ★ 与取消同一把计费锁、同一个条件（见 CancelSubscriptionAsync）。后台续费抢到这条订阅之后、
        // 与渠道往返的那几秒里，变更不看锁就会与续费各做各的：续费按旧价扣、这里把计划换掉，
        // 用户以旧价拿到新计划一整期。锁最多持有 BillingLockMinutes 分钟，因此是 409 不是 400。
        var now = DateTime.UtcNow;
        if (!await TryClaimAsync(subscriptionId, now, now.AddMinutes(PaymentOptions.BillingLockMinutes), cancellationToken))
            return Fail<SubscriptionChangeDto>(ErrorCodes.SubscriptionBillingInProgress, 409);

        try
        {
            return await ChangeSubscriptionPlanCoreAsync(subscription, currentPlan, newPlan, input, cancellationToken);
        }
        finally
        {
            // 裸 SQL 释放而不是改跟踪实体：补差款 off-session 当场扣成功时，回流处理器已在别的 scope
            // 里把计划换掉了，拿这里的旧实体 UpdateAsync 会把那次变更覆盖回去
            await ReleaseBillingLockAsync(subscriptionId, cancellationToken);
        }
    }

    private async Task<Result<SubscriptionChangeDto>> ChangeSubscriptionPlanCoreAsync(
        Subscription subscription, SubscriptionPlan currentPlan, SubscriptionPlan newPlan, ChangeSubscriptionPlanDto input, CancellationToken cancellationToken)
    {
        var subscriptionId = subscription.Id;
        var changeType = DetermineChangeType(currentPlan.Price, newPlan.Price);
        var proratedAmount = CalculateProratedAmount(subscription, currentPlan, newPlan);

        // 升级立即生效，降级周期结束生效
        var isImmediate = changeType == SubscriptionChangeType.Upgrade && input.EffectiveImmediately;
        var needsPayment = isImmediate && proratedAmount > 0;
        var effectiveDate = isImmediate
            ? DateTime.UtcNow
            : subscription.NextBillingTime ?? DateTime.UtcNow;

        // 需要收款的立即升级：渠道不能无人值守扣款时，ChargeOffSessionAsync 在建单之前就返回失败，
        // 一条支付事件都不会有，变更会悬在一个永远等不到回流的状态里。与 ChargeSubscriptionAsync
        // 同款的前置守卫：在写任何东西之前就拒绝（未绑卡的走待支付单，不受此限）。
        if (needsPayment && !string.IsNullOrWhiteSpace(subscription.PaymentMethodToken))
        {
            var provider = _paymentProviderFactory.GetProvider(subscription.ChannelCode);
            if (provider == null || !provider.SupportsOffSessionCharge)
                return Fail<SubscriptionChangeDto>(ErrorCodes.PaymentOffSessionNotSupported, 400);
        }

        var change = new SubscriptionChange
        {
            SubscriptionId = subscriptionId,
            FromPlanId = currentPlan.Id,
            ToPlanId = newPlan.Id,
            ChangeType = changeType,
            ProratedAmount = proratedAmount,
            EffectiveDate = effectiveDate,
            Status = needsPayment ? SubscriptionChangeStatus.AwaitingPayment : SubscriptionChangeStatus.Pending
        };

        await _changeRepository.InsertAsync(change, cancellationToken);

        // 立即生效升级：需补差价时先收款，收款确认后再应用计划变更（见 ApplyProrationChangeAsync）；
        // 无需补差则直接应用并标记已生效
        PaymentOrderResultDto? prorationPayment = null;
        if (needsPayment)
        {
            var charged = await ChargeOrCreateProrationPaymentAsync(
                subscription, currentPlan, newPlan, change.Id, proratedAmount, cancellationToken);

            if (!charged.Succeeded)
            {
                // off-session 扣款被拒 / 待支付单建不出来：变更不能留在等钱的状态里。
                // 失败回流可能已经在别的 scope 里把它置成 Cancelled，这里用条件更新兜底，幂等。
                await _changeRepository.AsQueryable()
                    .Where(c => c.Id == change.Id && c.Status == SubscriptionChangeStatus.AwaitingPayment)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SubscriptionChangeStatus.Cancelled), cancellationToken);

                return Fail<SubscriptionChangeDto>(charged.Message ?? ErrorCodes.PaymentCreationFailed, charged.Code ?? 400);
            }

            prorationPayment = charged.Data;

            // 待支付单的流水号记到变更行上：取消变更 / 周期结束时凭它关单。裸 SQL 只写这一列 ——
            // 失败回流可能已在别的 scope 里把 Status 改掉，UpdateAsync 会把跟踪实体上的旧状态写回去。
            if (prorationPayment != null)
            {
                change.PaymentTradeNo = prorationPayment.TradeNo;
                await _changeRepository.AsQueryable()
                    .Where(c => c.Id == change.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.PaymentTradeNo, prorationPayment.TradeNo), cancellationToken);
            }
        }
        else if (isImmediate)
        {
            await ApplyPlanChangeAsync(subscription, newPlan, cancellationToken);
            change.Status = SubscriptionChangeStatus.Applied;
            await _changeRepository.UpdateAsync(change, cancellationToken);
        }

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new SubscriptionPlanChangedEvent
            {
                SubscriptionId = subscriptionId,
                SubscriptionNo = subscription.SubscriptionNo,
                UserId = subscription.UserId,
                FromPlanId = currentPlan.Id,
                ToPlanId = newPlan.Id,
                ChangeType = changeType,
                ProratedAmount = proratedAmount,
                EffectiveDate = effectiveDate,
                Immediate = isImmediate
            });
        }

        Logger.LogInformation(
            "Subscription plan change created. SubscriptionNo: {SubscriptionNo}, ChangeType: {ChangeType}, From: {FromPlan}, To: {ToPlan}, Immediate: {Immediate}",
            subscription.SubscriptionNo, changeType, currentPlan.PlanName, newPlan.PlanName, isImmediate);

        // 状态以数据库为准：off-session 当场扣成功时，回流处理器已把这条变更置成 Applied
        var current = await _changeRepository.AsNoTracking()
            .Where(c => c.Id == change.Id)
            .Select(c => (SubscriptionChangeStatus?)c.Status)
            .FirstOrDefaultAsync(cancellationToken);
        change.Status = current ?? change.Status;

        var dto = BuildChangeDto(change, currentPlan, newPlan);
        dto.Payment = prorationPayment;
        return Ok(dto);
    }

    public async Task<Result<SubscriptionChangeDto>> GetPlanChangePreviewAsync(Guid subscriptionId, Guid newPlanId, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var context = await LoadChangeContextAsync(subscriptionId, newPlanId, ownerUserId, cancellationToken);
        if (!context.Succeeded || context.Data == null)
            return Fail<SubscriptionChangeDto>(context.Message ?? ErrorCodes.SubscriptionNotFound, context.Code ?? 400);

        var (subscription, currentPlan, newPlan) = (context.Data.Subscription, context.Data.CurrentPlan, context.Data.NewPlan);

        var changeType = DetermineChangeType(currentPlan.Price, newPlan.Price);
        var proratedAmount = CalculateProratedAmount(subscription, currentPlan, newPlan);
        var isImmediate = changeType == SubscriptionChangeType.Upgrade;

        return Ok(new SubscriptionChangeDto
        {
            SubscriptionId = subscriptionId,
            FromPlanId = currentPlan.Id,
            FromPlanName = currentPlan.PlanName,
            ToPlanId = newPlan.Id,
            ToPlanName = newPlan.PlanName,
            ChangeType = changeType,
            ProratedAmount = proratedAmount,
            EffectiveDate = isImmediate ? DateTime.UtcNow : subscription.NextBillingTime ?? DateTime.UtcNow,
            Status = isImmediate && proratedAmount > 0
                ? SubscriptionChangeStatus.AwaitingPayment
                : SubscriptionChangeStatus.Pending
        });
    }

    public async Task<Result> CancelPendingChangeAsync(Guid changeId, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var change = await _changeRepository
            .Where(c => c.Id == changeId)
            .Include(c => c.Subscription)
            .FirstOrDefaultAsync(cancellationToken);
        if (change == null)
            return Fail(ErrorCodes.SubscriptionChangeNotFound, 404);

        if (ownerUserId.HasValue && change.Subscription?.UserId != ownerUserId.Value)
            return Fail(ErrorCodes.SubscriptionChangeNotFound, 404);

        // 等补差款的变更同样可以反悔（用户还没付）。
        if (change.Status is not (SubscriptionChangeStatus.Pending or SubscriptionChangeStatus.AwaitingPayment))
            return Fail(ErrorCodes.SubscriptionChangeCannotCancel, 400);

        change.Status = SubscriptionChangeStatus.Cancelled;
        await _changeRepository.UpdateAsync(change, cancellationToken);

        // 连带关掉它的待支付单：不关，用户取消之后再付那张单就是一笔无处生效的孤儿付款。
        await CloseChangePaymentAsync(change, "plan change cancelled", cancellationToken);

        Logger.LogInformation("Pending subscription change cancelled. ChangeId: {ChangeId}", changeId);

        return Ok();
    }

    /// <summary>
    /// 关掉变更所属的补差待支付单。关单先让渠道侧失效（能作废的作废、不能的先查渠道状态），再本地 CAS：
    /// 渠道已经收到钱的会被记成功而不是关掉（PAYMENT_ALREADY_PAID），渠道不可达的留着不关（PAYMENT_CHANNEL_STATE_UNKNOWN）；
    /// 两种情况下那笔钱回流都会撞上一条不再 AwaitingPayment 的变更，按孤儿付款告警处理（见 ApplyProrationChangeAsync）。
    /// </summary>
    private async Task CloseChangePaymentAsync(SubscriptionChange change, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(change.PaymentTradeNo))
            return;

        var closed = await _paymentService.ClosePaymentAsync(change.PaymentTradeNo, reason, cancellationToken: cancellationToken);
        if (!closed.Succeeded)
        {
            Logger.LogWarning(
                "Proration payment {TradeNo} for change {ChangeId} could not be closed ({Message}, {Code}): it is already paid, in flight, or the channel could not confirm; if it completes it will be reported as an orphan payment.",
                change.PaymentTradeNo, change.Id, closed.Message, closed.Code);
        }
    }

    /// <summary>
    /// 周期结束（续费 / 试用转正成功 / 立即取消 / 过期）时，仍在等补差款的变更一律取消并关单：补差是对上一周期
    /// 剩余段算的，周期一过它就失效。留着它，迟到的补差款要么把新计划套在一个已按旧价收款的新周期上
    /// （新计划整期按旧价收），要么撞上已终止订阅的终态守卫变成一笔只剩告警的孤儿付款。
    /// 用户想升级重新发起即可，此时若已绑卡则当场扣款。
    /// </summary>
    private async Task CancelAwaitingChangesForEndedPeriodAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        var awaiting = await _changeRepository.ToListAsync(
            c => c.SubscriptionId == subscriptionId && c.Status == SubscriptionChangeStatus.AwaitingPayment, cancellationToken);

        foreach (var change in awaiting)
        {
            // CAS：补差款回流可能同时正把它置成 Applied，抢不到就不关它的单
            var cancelled = await _changeRepository.AsQueryable()
                .Where(c => c.Id == change.Id && c.Status == SubscriptionChangeStatus.AwaitingPayment)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SubscriptionChangeStatus.Cancelled), cancellationToken);
            if (cancelled == 0)
                continue;

            await CloseChangePaymentAsync(change, "billing period ended before the proration was paid", cancellationToken);
            Logger.LogInformation(
                "Awaiting-payment plan change {ChangeId} cancelled: the billing period of subscription {SubscriptionId} ended before its proration was paid.",
                change.Id, subscriptionId);
        }
    }

    public async Task<Result<int>> ApplyDuePlanChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var dueChangeIds = await _changeRepository.AsNoTracking()
            .Where(c => c.Status == SubscriptionChangeStatus.Pending && c.EffectiveDate <= now)
            .OrderBy(c => c.EffectiveDate)
            .Take(BillingScanPageSize)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (dueChangeIds.Count == 0)
            return Ok(0);

        var applied = 0;
        foreach (var changeId in dueChangeIds)
        {
            try
            {
                if (await ApplyDueChangeAsync(changeId, now, cancellationToken) != null)
                    applied++;
            }
            catch (Exception ex)
            {
                // 一条结算失败不能拖垮同一轮里其它订阅的变更
                Logger.LogError(ex, "Applying a due subscription plan change failed. ChangeId: {ChangeId}", changeId);
            }
        }

        if (applied > 0)
            Logger.LogInformation("Applied {Count} due subscription plan changes", applied);

        return Ok(applied);
    }

    /// <summary>
    /// 结算某条订阅上已到期的待生效变更，返回新生效的计划（没有到期变更时返回 null）。
    /// 供续费扫描与试用转正扫描在扣款**之前**调用（两处都是「周期边界 = 变更到期日」）。
    /// </summary>
    private async Task<SubscriptionPlan?> ApplyDuePlanChangeForSubscriptionAsync(
        Guid subscriptionId, DateTime now, CancellationToken cancellationToken)
    {
        var changeId = await _changeRepository.AsNoTracking()
            .Where(c => c.SubscriptionId == subscriptionId
                && c.Status == SubscriptionChangeStatus.Pending
                && c.EffectiveDate <= now)
            .OrderBy(c => c.EffectiveDate)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return changeId.HasValue
            ? await ApplyDueChangeAsync(changeId.Value, now, cancellationToken)
            : null;
    }

    /// <summary>
    /// 把一条到期的待生效变更真正应用到订阅上，返回新生效的计划；未能应用时返回 null。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>刻意不动 <c>NextBillingTime</c></b>。延后生效的变更恰好落在周期边界上，
    /// 那一刻的计费时钟由续费本身推进（<c>ApplyPaymentCompletedAsync</c> 从当前
    /// <c>NextBillingTime</c> 起算一个周期）。在这里顺手把它拨到「现在 + 一个周期」，
    /// 等于先白送一个未收款的周期，续费成功后又在此基础上再加一个 ——
    /// 一次降级换来两个周期的免费服务。立即生效的升级同样不动它（见类注释「保留计费锚点」）。
    /// </para>
    /// <para>
    /// ★ 抢占用 <b>CAS</b>（<c>WHERE Status = Pending</c> 原子改 <c>Applied</c>）而不是先读后写：
    /// 续费扫描与本扫描可能落在两个实例上同时命中同一条变更，先读后写会让两边都判定
    /// 「还没结算」，于是同一条变更发两次已生效事件。
    /// </para>
    /// </remarks>
    private async Task<SubscriptionPlan?> ApplyDueChangeAsync(Guid changeId, DateTime now, CancellationToken cancellationToken)
    {
        var claimed = await _changeRepository.AsQueryable()
            .Where(c => c.Id == changeId && c.Status == SubscriptionChangeStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SubscriptionChangeStatus.Applied), cancellationToken);

        if (claimed == 0)
            return null;

        var change = await _changeRepository.FirstOrDefaultAsync(c => c.Id == changeId, cancellationToken);
        if (change == null)
            return null;

        var subscription = await _subscriptionRepository.FirstOrDefaultAsync(s => s.Id == change.SubscriptionId, cancellationToken);
        var newPlan = await _planRepository.FirstOrDefaultAsync(p => p.Id == change.ToPlanId, cancellationToken);

        // 已终止的订阅不能被一条旧的待生效变更改写：那会给一个已取消的订阅换上新计划，
        // 让它在列表与报表里重新看起来像一份活订阅。计划被删掉同理 —— 无处可换。
        var terminated = subscription == null
            || subscription.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired;

        if (terminated || subscription == null || newPlan == null)
        {
            change.Status = SubscriptionChangeStatus.Cancelled;
            await _changeRepository.UpdateAsync(change, cancellationToken);

            Logger.LogInformation(
                "Due subscription plan change dropped. ChangeId: {ChangeId}, SubscriptionId: {SubscriptionId}, Reason: {Reason}",
                changeId, change.SubscriptionId, terminated ? "subscription is no longer active" : "target plan no longer exists");
            return null;
        }

        subscription.PlanId = newPlan.Id;
        // 不设 Plan 导航：仓储读出的计划是游离态，挂上去会被 EF 当新计划 INSERT（撞 PlanCode 唯一索引）
        subscription.ProductCode = newPlan.ProductCode;
        subscription.CycleType = newPlan.CycleType;
        subscription.CycleValue = newPlan.CycleValue;
        subscription.OriginalPrice = newPlan.Price;
        subscription.Currency = newPlan.Currency;
        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

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

        Logger.LogInformation(
            "Applied due subscription plan change. SubscriptionNo: {SubscriptionNo}, ChangeType: {ChangeType}, ToPlan: {ToPlan}",
            subscription.SubscriptionNo, change.ChangeType, newPlan.PlanName);

        return newPlan;
    }

    /// <summary>
    /// 计划变更上下文：变更与预览两条路径共用同一套前置校验，避免两处规则漂移
    /// </summary>
    private sealed record PlanChangeContext(Subscription Subscription, SubscriptionPlan CurrentPlan, SubscriptionPlan NewPlan);

    /// <summary>
    /// 计划变更的公共前置校验：订阅状态、计划有效性、币种与产品一致性
    /// </summary>
    private async Task<Result<PlanChangeContext>> LoadChangeContextAsync(
        Guid subscriptionId, Guid newPlanId, Guid? ownerUserId, CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository
            .Where(s => s.Id == subscriptionId && (!ownerUserId.HasValue || s.UserId == ownerUserId.Value))
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription == null)
            return Fail<PlanChangeContext>(ErrorCodes.SubscriptionNotFound, 404);

        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Trial))
            return Fail<PlanChangeContext>(ErrorCodes.SubscriptionAlreadyCancelledOrExpired, 400);

        if (subscription.PlanId == newPlanId)
            return Fail<PlanChangeContext>(ErrorCodes.SubscriptionSamePlan, 400);

        var newPlan = await _planRepository.FirstOrDefaultAsync(p => p.Id == newPlanId, cancellationToken);
        if (newPlan == null)
            return Fail<PlanChangeContext>(ErrorCodes.SubscriptionNewPlanNotFound, 404);

        if (!newPlan.IsActive)
            return Fail<PlanChangeContext>(ErrorCodes.SubscriptionPlanNotActive, 400);

        var currentPlan = subscription.Plan;
        if (currentPlan == null)
            return Fail<PlanChangeContext>(ErrorCodes.SubscriptionPlanNotFound, 404);

        if (!string.Equals(currentPlan.Currency, newPlan.Currency, StringComparison.OrdinalIgnoreCase))
            return Fail<PlanChangeContext>(ErrorCodes.SubscriptionCurrencyMismatch, 400);

        // 跨产品不是升降级，而是两笔独立订阅，按比例折算没有意义
        if (!string.Equals(currentPlan.ProductCode, newPlan.ProductCode, StringComparison.Ordinal))
            return Fail<PlanChangeContext>(ErrorCodes.SubscriptionNewPlanNotFound, 400);

        return Ok(new PlanChangeContext(subscription, currentPlan, newPlan));
    }

    /// <summary>
    /// 应用无需收款的立即变更到订阅（补差为 0、试用期内升级）。
    /// </summary>
    /// <remarks>
    /// ★ 不动 <c>NextBillingTime</c>：这里没有发生任何一次收款，把时钟拨到「现在 + 整周期」等于白送一期，
    /// 周期末尾补差四舍五入到 0 的那次升级还会顺带跳过本该发生的续费扣款（续费扫描要 NextBillingTime &lt;= now）。
    /// 锚点留在原处，到期由续费按新价收。
    /// </remarks>
    private async Task ApplyPlanChangeAsync(Subscription subscription, SubscriptionPlan newPlan, CancellationToken cancellationToken)
    {
        subscription.PlanId = newPlan.Id;
        // 不设 Plan 导航：仓储读出的计划是游离态，挂上去会被 EF 当新计划 INSERT（撞 PlanCode 唯一索引）
        subscription.ProductCode = newPlan.ProductCode;
        subscription.CycleType = newPlan.CycleType;
        subscription.CycleValue = newPlan.CycleValue;
        subscription.OriginalPrice = newPlan.Price;
        subscription.Currency = newPlan.Currency;

        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);
    }

    /// <summary>
    /// 释放计费锁（裸 SQL，不经跟踪实体）。
    /// </summary>
    private Task ReleaseBillingLockAsync(Guid subscriptionId, CancellationToken cancellationToken)
        => _subscriptionRepository.AsQueryable()
            .Where(s => s.Id == subscriptionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.BillingLockedUntil, (DateTime?)null), cancellationToken);

    /// <summary>
    /// 判断变更类型
    /// </summary>
    private static SubscriptionChangeType DetermineChangeType(decimal currentPrice, decimal newPrice)
    {
        if (newPrice > currentPrice) return SubscriptionChangeType.Upgrade;
        if (newPrice < currentPrice) return SubscriptionChangeType.Downgrade;
        return SubscriptionChangeType.CrossGrade;
    }

    /// <summary>
    /// 计算按比例金额：当前计划剩余天数的信用额度 vs 新计划剩余天数的费用
    /// </summary>
    private static decimal CalculateProratedAmount(Subscription subscription, SubscriptionPlan currentPlan, SubscriptionPlan newPlan)
    {
        // 试用期内一分钱没付过，「剩余比例 × 差价」补的是一段用户没买过的东西；
        // 计划立即换、试用截止不动，转正那一刻按新价收。
        if (subscription.Status == SubscriptionStatus.Trial)
            return 0m;

        var now = DateTime.UtcNow;
        var periodEnd = subscription.NextBillingTime ?? now;

        // 周期总时长（按 ticks 计算，全程 decimal，避免 double 中间值精度损失）
        var periodStart = CalculatePeriodStart(periodEnd, currentPlan.CycleType, currentPlan.CycleValue);
        var totalTicks = (periodEnd - periodStart).Ticks;

        // 周期长度算不出来（计划的周期值为 0 或负）→ 按「整个周期都还剩着」兜底，即两个计划的差额。
        // 此前这里返回新计划**全价**，方向是反的：一次降级会向用户收取新计划的整整一期费用，
        // 而正常路径上降级永远是负数（信用）。
        if (totalTicks <= 0)
            return CurrencyInfo.Round(newPlan.Price - currentPlan.Price, newPlan.Currency);

        // 剩余时长占比
        var remainingTicks = Math.Max(0L, (periodEnd - now).Ticks);
        var remainingRatio = (decimal)remainingTicks / totalTicks;

        // 当前计划剩余信用额度 vs 新计划剩余费用
        var credit = currentPlan.Price * remainingRatio;
        var charge = newPlan.Price * remainingRatio;

        // 差额：正数=需要补差价，负数=返还信用
        return CurrencyInfo.Round(charge - credit, newPlan.Currency);
    }

    /// <summary>
    /// 根据周期结束时间反推周期开始时间
    /// </summary>
    private static DateTime CalculatePeriodStart(DateTime periodEnd, BillingCycleType cycleType, int cycleValue)
    {
        return cycleType switch
        {
            BillingCycleType.Day => periodEnd.AddDays(-cycleValue),
            BillingCycleType.Week => periodEnd.AddDays(-7 * cycleValue),
            BillingCycleType.Month => periodEnd.AddMonths(-cycleValue),
            BillingCycleType.Year => periodEnd.AddYears(-cycleValue),
            _ => periodEnd.AddMonths(-1)
        };
    }

    /// <summary>
    /// 构建变更 DTO
    /// </summary>
    private static SubscriptionChangeDto BuildChangeDto(SubscriptionChange change, SubscriptionPlan fromPlan, SubscriptionPlan toPlan)
    {
        return new SubscriptionChangeDto
        {
            Id = change.Id,
            SubscriptionId = change.SubscriptionId,
            FromPlanId = change.FromPlanId,
            FromPlanName = fromPlan.PlanName,
            ToPlanId = change.ToPlanId,
            ToPlanName = toPlan.PlanName,
            ChangeType = change.ChangeType,
            ProratedAmount = change.ProratedAmount,
            EffectiveDate = change.EffectiveDate,
            Status = change.Status,
            PaymentTradeNo = change.PaymentTradeNo,
            CreationTime = change.CreationTime
        };
    }
}
