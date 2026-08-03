namespace Tnzi.Payment.Services;

/// <summary>
/// 订阅服务（partial）：计划变更（升降级）与按比例计费。
/// </summary>
/// <remarks>
/// 变更与预览共用同一套前置校验（<see cref="PlanChangeContext"/>），两条路径的规则因此不可能漂移。
/// 升级立即生效需先收补差款，收款确认后才应用计划（见 SubscriptionService.Billing.cs 的 ApplyProrationChangeAsync）；
/// 降级一律等到周期结束，避免用户在已付费周期内被降权。
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

        // 检查是否有待生效的变更
        var pendingChange = await _changeRepository.FirstOrDefaultAsync(
            c => c.SubscriptionId == subscriptionId && c.Status == SubscriptionChangeStatus.Pending, cancellationToken);
        if (pendingChange != null)
            return Fail<SubscriptionChangeDto>(ErrorCodes.SubscriptionChangePending, 400);

        // 计算按比例金额
        var changeType = DetermineChangeType(currentPlan.Price, newPlan.Price);
        var proratedAmount = CalculateProratedAmount(subscription, currentPlan, newPlan);

        // 升级立即生效，降级周期结束生效
        var isImmediate = changeType == SubscriptionChangeType.Upgrade && input.EffectiveImmediately;
        var effectiveDate = isImmediate
            ? DateTime.UtcNow
            : subscription.NextBillingTime ?? DateTime.UtcNow;

        var change = new SubscriptionChange
        {
            SubscriptionId = subscriptionId,
            FromPlanId = currentPlan.Id,
            ToPlanId = newPlan.Id,
            ChangeType = changeType,
            ProratedAmount = proratedAmount,
            EffectiveDate = effectiveDate,
            Status = SubscriptionChangeStatus.Pending
        };

        await _changeRepository.InsertAsync(change, cancellationToken);

        // 立即生效升级：需补差价时先收款，收款确认后再应用计划变更（见 ApplyProrationChangeAsync）；
        // 无需补差则直接应用并标记已生效
        PaymentOrderResultDto? prorationPayment = null;
        if (isImmediate)
        {
            if (proratedAmount > 0)
            {
                prorationPayment = await ChargeOrCreateProrationPaymentAsync(
                    subscription, currentPlan, newPlan, change.Id, proratedAmount, cancellationToken);
            }
            else
            {
                await ApplyPlanChangeAsync(subscription, newPlan, cancellationToken);
                change.Status = SubscriptionChangeStatus.Applied;
                await _changeRepository.UpdateAsync(change, cancellationToken);
            }
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
            Status = SubscriptionChangeStatus.Pending
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

        if (change.Status != SubscriptionChangeStatus.Pending)
            return Fail(ErrorCodes.SubscriptionChangeCannotCancel, 400);

        change.Status = SubscriptionChangeStatus.Cancelled;
        await _changeRepository.UpdateAsync(change, cancellationToken);

        Logger.LogInformation("Pending subscription change cancelled. ChangeId: {ChangeId}", changeId);

        return Ok();
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
    /// 应用计划变更到订阅
    /// </summary>
    private async Task ApplyPlanChangeAsync(Subscription subscription, SubscriptionPlan newPlan, CancellationToken cancellationToken)
    {
        subscription.PlanId = newPlan.Id;
        // 不设 Plan 导航：仓储读出的计划是游离态，挂上去会被 EF 当新计划 INSERT（撞 PlanCode 唯一索引）
        subscription.ProductCode = newPlan.ProductCode;
        subscription.CycleType = newPlan.CycleType;
        subscription.CycleValue = newPlan.CycleValue;
        subscription.OriginalPrice = newPlan.Price;
        subscription.Currency = newPlan.Currency;
        subscription.NextBillingTime = CalculateNextBillingTime(DateTime.UtcNow, newPlan.CycleType, newPlan.CycleValue);

        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);
    }

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
        var now = DateTime.UtcNow;
        var periodEnd = subscription.NextBillingTime ?? now;

        // 周期总时长（按 ticks 计算，全程 decimal，避免 double 中间值精度损失）
        var periodStart = CalculatePeriodStart(periodEnd, currentPlan.CycleType, currentPlan.CycleValue);
        var totalTicks = (periodEnd - periodStart).Ticks;
        if (totalTicks <= 0) return newPlan.Price;

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
            CreationTime = change.CreationTime
        };
    }
}
