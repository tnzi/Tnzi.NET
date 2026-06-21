namespace Tnzi.Payment.Services;

/// <summary>
/// 订阅服务实现
/// </summary>
public partial class SubscriptionService : ApplicationService, ISubscriptionService
{
    private readonly IRepository<Subscription, Guid> _subscriptionRepository;
    private readonly IRepository<SubscriptionPlan, Guid> _planRepository;
    private readonly IRepository<SubscriptionChange, Guid> _changeRepository;
    private readonly IPaymentService _paymentService;
    private readonly IPaymentProviderFactory _paymentProviderFactory;
    private readonly IOptionsMonitor<PaymentOptions> _paymentOptionsMonitor;

    private const int BillingScanPageSize = 200;

    private PaymentOptions PaymentOptions => _paymentOptionsMonitor.CurrentValue;

    public SubscriptionService(
        IRepository<Subscription, Guid> subscriptionRepository,
        IRepository<SubscriptionPlan, Guid> planRepository,
        IRepository<SubscriptionChange, Guid> changeRepository,
        IPaymentService paymentService,
        IPaymentProviderFactory paymentProviderFactory,
        IOptionsMonitor<PaymentOptions> paymentOptionsMonitor,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _subscriptionRepository = Check.NotNull(subscriptionRepository);
        _planRepository = Check.NotNull(planRepository);
        _changeRepository = Check.NotNull(changeRepository);
        _paymentService = Check.NotNull(paymentService);
        _paymentProviderFactory = Check.NotNull(paymentProviderFactory);
        _paymentOptionsMonitor = Check.NotNull(paymentOptionsMonitor);
    }

    public async Task<Result<SubscriptionDto>> CreateSubscriptionAsync(CreateSubscriptionDto request, CancellationToken cancellationToken = default)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var plan = await _planRepository.FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);

        if (plan == null)
            return Fail<SubscriptionDto>(ErrorCodes.SubscriptionPlanNotFound, 404);

        if (!plan.IsActive)
            return Fail<SubscriptionDto>(ErrorCodes.SubscriptionPlanNotActive, 400);

        // 检查是否有未完成的订阅
        var existingSubscription = await _subscriptionRepository.FirstOrDefaultAsync(
            s => s.UserId == userId && s.Status != SubscriptionStatus.Cancelled && s.Status != SubscriptionStatus.Expired,
            cancellationToken);

        if (existingSubscription != null)
            return Fail<SubscriptionDto>(ErrorCodes.SubscriptionAlreadyActive, 400);

        var subscription = new Subscription
        {
            SubscriptionNo = Subscription.GenerateSubscriptionNo(),
            UserId = userId,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Pending,
            CycleType = plan.CycleType,
            CycleValue = plan.CycleValue,
            StartTime = DateTime.UtcNow,
            EndTime = null,
            OriginalPrice = plan.Price,
            PaidAmount = 0,
            DiscountAmount = 0,
            Currency = plan.Currency,
            ChannelCode = request.ChannelCode ?? PaymentConstants.DefaultPaymentChannel,
            AutoRenew = true,
            Plan = plan
        };

        // 处理试用
        if (request.EnableTrial && plan.AllowTrial && plan.TrialDays > 0)
        {
            subscription.Status = SubscriptionStatus.Trial;
            subscription.TrialStartTime = DateTime.UtcNow;
            subscription.TrialEndTime = DateTime.UtcNow.AddDays(plan.TrialDays);
            subscription.DiscountAmount = plan.TrialDiscount ?? 0;
            subscription.PaidAmount = plan.Price - subscription.DiscountAmount;
            // 试用期结束后的首次计费时间
            subscription.NextBillingTime = subscription.TrialEndTime;
        }
        else
        {
            // 创建首次支付：订阅保持 Pending，待支付完成事件回流后才激活（见 ApplyPaymentCompletedAsync）
            var paymentResult = await _paymentService.CreatePaymentAsync(new CreatePaymentDto
            {
                BusinessOrderNo = subscription.SubscriptionNo,
                BusinessType = BusinessType.Subscription,
                Amount = plan.Price - subscription.DiscountAmount,
                Currency = plan.Currency,
                ChannelCode = request.ChannelCode,
                Description = $"Subscription: {plan.PlanName}",
                ExtraData = new SubscriptionBillingMetadata { Purpose = SubscriptionBillingPurpose.Initial }.ToExtraData()
            }, cancellationToken);

            if (!paymentResult.Succeeded)
                return Fail<SubscriptionDto>(paymentResult.Message ?? ErrorCodes.PaymentCreationFailed);

            subscription.Status = SubscriptionStatus.Pending;
            subscription.NextBillingTime = CalculateNextBillingTime(DateTime.UtcNow, plan.CycleType, plan.CycleValue);
        }

        await _subscriptionRepository.InsertAsync(subscription, cancellationToken);

        // 发布订阅创建事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new SubscriptionCreatedEvent
            {
                SubscriptionId = subscription.Id,
                SubscriptionNo = subscription.SubscriptionNo,
                UserId = userId,
                PlanId = plan.Id,
                PlanName = plan.PlanName,
                StartTime = subscription.StartTime,
                EndTime = subscription.EndTime,
                IsTrial = subscription.Status == SubscriptionStatus.Trial,
                TrialEndTime = subscription.TrialEndTime
            });
        }

        Logger.LogInformation("Subscription created. UserId: {UserId}, Plan: {PlanName}, Trial: {IsTrial}",
            userId, plan.PlanName, subscription.Status == SubscriptionStatus.Trial);

        return Ok(subscription.MapTo<SubscriptionDto>());
    }

    public async Task<Result> CancelSubscriptionAsync(Guid subscriptionId, CancelSubscriptionDto request, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var subscription = await _subscriptionRepository.FirstOrDefaultAsync(
            s => s.Id == subscriptionId && (!ownerUserId.HasValue || s.UserId == ownerUserId.Value), cancellationToken);
        if (subscription == null)
            return Fail(ErrorCodes.SubscriptionNotFound, 404);

        if (subscription.Status == SubscriptionStatus.Cancelled || subscription.Status == SubscriptionStatus.Expired)
            return Fail(ErrorCodes.SubscriptionAlreadyCancelledOrExpired, 400);

        subscription.CancelReason = request.Reason;
        subscription.CancelTime = DateTime.UtcNow;

        if (request.Immediate)
        {
            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.EndTime = DateTime.UtcNow;
        }
        else
        {
            // 到期后取消：关闭自动续费，到期自然过期
            subscription.Status = SubscriptionStatus.PendingRenewal;
            subscription.AutoRenew = false;
        }

        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

        // 发布订阅取消事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new SubscriptionCancelledEvent
            {
                SubscriptionId = subscription.Id,
                SubscriptionNo = subscription.SubscriptionNo,
                UserId = subscription.UserId,
                CancelReason = request.Reason,
                Immediate = request.Immediate,
                ExpireTime = subscription.EndTime
            });
        }

        Logger.LogInformation("Subscription cancelled. SubscriptionNo: {SubscriptionNo}, Immediate: {Immediate}",
            subscription.SubscriptionNo, request.Immediate);

        return Ok();
    }

    public async Task<Result> ResumeSubscriptionAsync(Guid subscriptionId, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var subscription = await _subscriptionRepository.FirstOrDefaultAsync(
            s => s.Id == subscriptionId && (!ownerUserId.HasValue || s.UserId == ownerUserId.Value), cancellationToken);
        if (subscription == null)
            return Fail(ErrorCodes.SubscriptionNotFound, 404);

        if (subscription.Status != SubscriptionStatus.Cancelled && subscription.Status != SubscriptionStatus.Paused)
            return Fail(ErrorCodes.SubscriptionCannotResume, 400);

        subscription.Status = SubscriptionStatus.Active;
        subscription.AutoRenew = true;
        subscription.CancelReason = null;
        subscription.CancelTime = null;

        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

        Logger.LogInformation("Subscription resumed. SubscriptionNo: {SubscriptionNo}", subscription.SubscriptionNo);

        return Ok();
    }

    public async Task<Result<SubscriptionDto>> ChangePlanAsync(Guid subscriptionId, ChangeSubscriptionDto request, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var subscription = await _subscriptionRepository.FirstOrDefaultAsync(
            s => s.Id == subscriptionId && (!ownerUserId.HasValue || s.UserId == ownerUserId.Value), cancellationToken);
        if (subscription == null)
            return Fail<SubscriptionDto>(ErrorCodes.SubscriptionNotFound, 404);

        if (subscription.Status != SubscriptionStatus.Active && subscription.Status != SubscriptionStatus.Trial)
            return Fail<SubscriptionDto>(ErrorCodes.SubscriptionAlreadyCancelledOrExpired, 400);

        var newPlan = await _planRepository.FirstOrDefaultAsync(p => p.Id == request.NewPlanId, cancellationToken);
        if (newPlan == null)
            return Fail<SubscriptionDto>(ErrorCodes.SubscriptionNewPlanNotFound, 404);

        if (!newPlan.IsActive)
            return Fail<SubscriptionDto>(ErrorCodes.SubscriptionPlanNotActive, 400);

        if (subscription.PlanId == newPlan.Id)
            return Fail<SubscriptionDto>(ErrorCodes.SubscriptionPlanNotFound, 400);

        // 更新订阅计划
        subscription.PlanId = newPlan.Id;
        subscription.Plan = newPlan;
        subscription.CycleType = newPlan.CycleType;
        subscription.CycleValue = newPlan.CycleValue;
        subscription.OriginalPrice = newPlan.Price;
        subscription.Currency = newPlan.Currency;

        // 重新计算下次计费时间
        subscription.NextBillingTime = CalculateNextBillingTime(DateTime.UtcNow, newPlan.CycleType, newPlan.CycleValue);

        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

        Logger.LogInformation("Subscription plan changed. SubscriptionNo: {SubscriptionNo}, NewPlan: {PlanName}",
            subscription.SubscriptionNo, newPlan.PlanName);

        return Ok(subscription.MapTo<SubscriptionDto>());
    }

    public async Task<Result> UpdatePaymentMethodAsync(Guid subscriptionId, string paymentMethodId, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var subscription = await _subscriptionRepository.FirstOrDefaultAsync(
            s => s.Id == subscriptionId && (!ownerUserId.HasValue || s.UserId == ownerUserId.Value), cancellationToken);
        if (subscription == null)
            return Fail(ErrorCodes.SubscriptionNotFound, 404);

        var provider = _paymentProviderFactory.GetProvider(subscription.ChannelCode);
        if (provider == null)
            return Fail(ErrorCodes.PaymentChannelNotSupported, 400);

        var result = await provider.UpdatePaymentMethodAsync(subscription.SubscriptionNo, paymentMethodId);
        if (!result.Succeeded)
            return Fail(result.Message ?? ErrorCodes.PaymentChannelNotSupported);

        Logger.LogInformation("Subscription payment method updated. SubscriptionNo: {SubscriptionNo}", subscription.SubscriptionNo);

        return Ok();
    }

    public async Task<Result> UpdateAutoRenewAsync(Guid subscriptionId, bool autoRenew, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var subscription = await _subscriptionRepository.FirstOrDefaultAsync(
            s => s.Id == subscriptionId && (!ownerUserId.HasValue || s.UserId == ownerUserId.Value), cancellationToken);
        if (subscription == null)
            return Fail(ErrorCodes.SubscriptionNotFound, 404);

        subscription.AutoRenew = autoRenew;
        if (!autoRenew && subscription.Status == SubscriptionStatus.Active)
        {
            subscription.Status = SubscriptionStatus.PendingRenewal;
        }
        else if (autoRenew && subscription.Status == SubscriptionStatus.PendingRenewal)
        {
            subscription.Status = SubscriptionStatus.Active;
        }

        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

        Logger.LogInformation("Subscription auto-renewal updated. SubscriptionNo: {SubscriptionNo}, AutoRenew: {AutoRenew}",
            subscription.SubscriptionNo, autoRenew);

        return Ok();
    }

    public async Task<Result<SubscriptionDto>> GetSubscriptionAsync(Guid id, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var subscription = await _subscriptionRepository.FirstOrDefaultAsync(
            s => s.Id == id && (!ownerUserId.HasValue || s.UserId == ownerUserId.Value), cancellationToken);
        if (subscription == null)
            return Fail<SubscriptionDto>(ErrorCodes.SubscriptionNotFound, 404);

        return Ok(subscription.MapTo<SubscriptionDto>());
    }

    public async Task<Result<IPagedList<SubscriptionDto>>> GetUserSubscriptionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var pagedList = await _subscriptionRepository.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreationTime)
            .ProjectTo<Subscription, SubscriptionDto>()
            .CreateAsync(1, 100, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<IPagedList<SubscriptionDto>>> GetSubscriptionListAsync(SubscriptionQueryDto query, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var queryable = _subscriptionRepository.AsNoTracking().Filter(query);

        if (ownerUserId.HasValue)
            queryable = queryable.Where(s => s.UserId == ownerUserId.Value);

        var pagedList = await queryable
            .OrderByDescending(s => s.CreationTime)
            .ProjectTo<Subscription, SubscriptionDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<List<SubscriptionPlanDto>>> GetSubscriptionPlansAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var queryable = _planRepository.AsNoTracking();

        if (activeOnly)
            queryable = queryable.Where(p => p.IsActive);

        var plans = await queryable.OrderBy(p => p.SortOrder).ToListAsync(cancellationToken);

        return Ok(plans.MapToList<SubscriptionPlanDto>());
    }

    public async Task<Result<SubscriptionPlanDto>> CreatePlanAsync(SubscriptionPlanDto planDto, CancellationToken cancellationToken = default)
    {
        var plan = planDto.MapTo<SubscriptionPlan>();
        await _planRepository.InsertAsync(plan, cancellationToken);

        Logger.LogInformation("Subscription plan created. PlanName: {PlanName}", plan.PlanName);

        return Ok(plan.MapTo<SubscriptionPlanDto>());
    }

    public async Task<Result> UpdatePlanAsync(Guid planId, SubscriptionPlanDto planDto, CancellationToken cancellationToken = default)
    {
        var plan = await _planRepository.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan == null)
            return Fail(ErrorCodes.SubscriptionPlanNotFound, 404);

        plan.PlanName = planDto.PlanName;
        plan.Description = planDto.Description;
        plan.Price = planDto.Price;
        plan.Currency = planDto.Currency;
        plan.CycleType = planDto.CycleType;
        plan.CycleValue = planDto.CycleValue;
        plan.TrialDays = planDto.TrialDays;
        plan.IsActive = planDto.IsActive;
        plan.AllowTrial = planDto.AllowTrial;
        plan.TrialDiscount = planDto.TrialDiscount;
        plan.SortOrder = planDto.SortOrder;

        await _planRepository.UpdateAsync(plan, cancellationToken);

        Logger.LogInformation("Subscription plan updated. PlanId: {PlanId}", planId);

        return Ok();
    }

    public async Task<Result> DeletePlanAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        var plan = await _planRepository.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan == null)
            return Fail(ErrorCodes.SubscriptionPlanNotFound, 404);

        // 检查是否有使用该计划的活跃订阅
        var activeSubscriptions = await _subscriptionRepository.CountAsync(
            s => s.PlanId == planId && s.Status != SubscriptionStatus.Cancelled && s.Status != SubscriptionStatus.Expired,
            cancellationToken);

        if (activeSubscriptions > 0)
            return Fail(ErrorCodes.SubscriptionPlanHasActiveSubscriptions, 400);

        plan.IsActive = false;
        await _planRepository.UpdateAsync(plan, cancellationToken);

        Logger.LogInformation("Subscription plan deactivated. PlanId: {PlanId}", planId);

        return Ok();
    }

    public async Task<Result<SubscriptionChangeDto>> ChangeSubscriptionPlanAsync(Guid subscriptionId, ChangeSubscriptionPlanDto input, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var subscription = await _subscriptionRepository
            .Where(s => s.Id == subscriptionId && (!ownerUserId.HasValue || s.UserId == ownerUserId.Value))
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(cancellationToken);
        if (subscription == null)
            return Fail<SubscriptionChangeDto>(ErrorCodes.SubscriptionNotFound, 404);

        if (subscription.Status != SubscriptionStatus.Active && subscription.Status != SubscriptionStatus.Trial)
            return Fail<SubscriptionChangeDto>(ErrorCodes.SubscriptionAlreadyCancelledOrExpired, 400);

        if (subscription.PlanId == input.NewPlanId)
            return Fail<SubscriptionChangeDto>(ErrorCodes.SubscriptionSamePlan, 400);

        var newPlan = await _planRepository.FirstOrDefaultAsync(p => p.Id == input.NewPlanId, cancellationToken);
        if (newPlan == null)
            return Fail<SubscriptionChangeDto>(ErrorCodes.SubscriptionNewPlanNotFound, 404);

        if (!newPlan.IsActive)
            return Fail<SubscriptionChangeDto>(ErrorCodes.SubscriptionPlanNotActive, 400);

        var currentPlan = subscription.Plan;
        if (currentPlan == null)
            return Fail<SubscriptionChangeDto>(ErrorCodes.SubscriptionPlanNotFound, 404);

        if (!string.Equals(currentPlan.Currency, newPlan.Currency, StringComparison.OrdinalIgnoreCase))
            return Fail<SubscriptionChangeDto>(ErrorCodes.SubscriptionCurrencyMismatch, 400);

        // 检查是否有待生效的变更
        var pendingChange = await _changeRepository.FirstOrDefaultAsync(
            c => c.SubscriptionId == subscriptionId && c.Status == SubscriptionChangeStatus.Pending, cancellationToken);
        if (pendingChange != null)
            return Fail<SubscriptionChangeDto>(ErrorCodes.SubscriptionChangePending, 400);

        // 计算按比例金额
        var changeType = DetermineChangeType(currentPlan.Price, newPlan.Price);
        var proratedAmount = CalculateProratedAmount(subscription, currentPlan, newPlan);

        // 升级立即生效，降级周期结束生效
        var isImmediate = changeType == SubscriptionChangeType.Upgrade
            ? input.EffectiveImmediately
            : false;
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
        if (isImmediate)
        {
            if (proratedAmount > 0)
            {
                await ChargeOrCreateProrationPaymentAsync(subscription, currentPlan, newPlan, change.Id, proratedAmount, cancellationToken);
            }
            else
            {
                await ApplyPlanChangeAsync(subscription, newPlan, cancellationToken);
                change.Status = SubscriptionChangeStatus.Applied;
                await _changeRepository.UpdateAsync(change, cancellationToken);
            }
        }

        // 发布事件
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

        return Ok(BuildChangeDto(change, currentPlan, newPlan));
    }

    public async Task<Result<SubscriptionChangeDto>> GetPlanChangePreviewAsync(Guid subscriptionId, Guid newPlanId, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var subscription = await _subscriptionRepository
            .Where(s => s.Id == subscriptionId && (!ownerUserId.HasValue || s.UserId == ownerUserId.Value))
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(cancellationToken);
        if (subscription == null)
            return Fail<SubscriptionChangeDto>(ErrorCodes.SubscriptionNotFound, 404);

        if (subscription.Status != SubscriptionStatus.Active && subscription.Status != SubscriptionStatus.Trial)
            return Fail<SubscriptionChangeDto>(ErrorCodes.SubscriptionAlreadyCancelledOrExpired, 400);

        if (subscription.PlanId == newPlanId)
            return Fail<SubscriptionChangeDto>(ErrorCodes.SubscriptionSamePlan, 400);

        var newPlan = await _planRepository.FirstOrDefaultAsync(p => p.Id == newPlanId, cancellationToken);
        if (newPlan == null)
            return Fail<SubscriptionChangeDto>(ErrorCodes.SubscriptionNewPlanNotFound, 404);

        if (!newPlan.IsActive)
            return Fail<SubscriptionChangeDto>(ErrorCodes.SubscriptionPlanNotActive, 400);

        var currentPlan = subscription.Plan;
        if (currentPlan == null)
            return Fail<SubscriptionChangeDto>(ErrorCodes.SubscriptionPlanNotFound, 404);

        if (!string.Equals(currentPlan.Currency, newPlan.Currency, StringComparison.OrdinalIgnoreCase))
            return Fail<SubscriptionChangeDto>(ErrorCodes.SubscriptionCurrencyMismatch, 400);

        var changeType = DetermineChangeType(currentPlan.Price, newPlan.Price);
        var proratedAmount = CalculateProratedAmount(subscription, currentPlan, newPlan);
        var isImmediate = changeType == SubscriptionChangeType.Upgrade;
        var effectiveDate = isImmediate
            ? DateTime.UtcNow
            : subscription.NextBillingTime ?? DateTime.UtcNow;

        var preview = new SubscriptionChangeDto
        {
            SubscriptionId = subscriptionId,
            FromPlanId = currentPlan.Id,
            FromPlanName = currentPlan.PlanName,
            ToPlanId = newPlan.Id,
            ToPlanName = newPlan.PlanName,
            ChangeType = changeType,
            ProratedAmount = proratedAmount,
            EffectiveDate = effectiveDate,
            Status = SubscriptionChangeStatus.Pending
        };

        return Ok(preview);
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
    /// 应用计划变更到订阅
    /// </summary>
    private async Task ApplyPlanChangeAsync(Subscription subscription, SubscriptionPlan newPlan, CancellationToken cancellationToken)
    {
        subscription.PlanId = newPlan.Id;
        subscription.Plan = newPlan;
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
        return Math.Round(charge - credit, 2, MidpointRounding.AwayFromZero);
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

    /// <summary>
    /// 计算下次计费时间
    /// </summary>
    private static DateTime CalculateNextBillingTime(DateTime from, BillingCycleType cycleType, int cycleValue)
    {
        return cycleType switch
        {
            BillingCycleType.Day => from.AddDays(cycleValue),
            BillingCycleType.Week => from.AddDays(7 * cycleValue),
            BillingCycleType.Month => from.AddMonths(cycleValue),
            BillingCycleType.Year => from.AddYears(cycleValue),
            _ => from.AddMonths(1)
        };
    }
}
