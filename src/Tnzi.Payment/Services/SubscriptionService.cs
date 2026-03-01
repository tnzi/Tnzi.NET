namespace Tnzi.Payment.Services;

/// <summary>
/// 订阅服务实现
/// </summary>
public class SubscriptionService : ApplicationService, ISubscriptionService
{
    private readonly IRepository<Subscription, Guid> _subscriptionRepository;
    private readonly IRepository<SubscriptionPlan, Guid> _planRepository;
    private readonly IPaymentService _paymentService;
    private readonly IPaymentProviderFactory _paymentProviderFactory;

    public SubscriptionService(
        IRepository<Subscription, Guid> subscriptionRepository,
        IRepository<SubscriptionPlan, Guid> planRepository,
        IPaymentService paymentService,
        IPaymentProviderFactory paymentProviderFactory,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _subscriptionRepository = Check.NotNull(subscriptionRepository);
        _planRepository = Check.NotNull(planRepository);
        _paymentService = Check.NotNull(paymentService);
        _paymentProviderFactory = Check.NotNull(paymentProviderFactory);
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
            // 创建首次支付
            var paymentResult = await _paymentService.CreatePaymentAsync(new CreatePaymentDto
            {
                BusinessOrderNo = subscription.SubscriptionNo,
                BusinessType = BusinessType.Subscription,
                Amount = plan.Price - subscription.DiscountAmount,
                Currency = plan.Currency,
                ChannelCode = request.ChannelCode,
                Description = $"Subscription: {plan.PlanName}"
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

    public async Task<Result<int>> RenewExpiredSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // 查找到期需要续费的订阅
        var dueSubscriptions = await _subscriptionRepository
            .Where(s => s.AutoRenew
                && s.Status == SubscriptionStatus.Active
                && s.NextBillingTime != null
                && s.NextBillingTime <= now)
            .Include(s => s.Plan)
            .ToListAsync(cancellationToken);

        if (dueSubscriptions.Count == 0)
            return Ok(0);

        var renewedCount = 0;

        foreach (var subscription in dueSubscriptions)
        {
            try
            {
                if (subscription.Plan == null)
                    continue;

                // 创建续费支付
                var paymentResult = await _paymentService.CreatePaymentAsync(new CreatePaymentDto
                {
                    BusinessOrderNo = subscription.SubscriptionNo,
                    BusinessType = BusinessType.Subscription,
                    Amount = subscription.Plan.Price,
                    Currency = subscription.Currency,
                    ChannelCode = subscription.ChannelCode,
                    Description = $"Subscription renewal: {subscription.Plan.PlanName}"
                }, cancellationToken);

                if (!paymentResult.Succeeded)
                {
                    Logger.LogWarning("Subscription renewal payment failed. SubscriptionNo: {SubscriptionNo}",
                        subscription.SubscriptionNo);
                    continue;
                }

                // 更新下次计费时间
                var nextBillingTime = CalculateNextBillingTime(now, subscription.CycleType, subscription.CycleValue);
                subscription.NextBillingTime = nextBillingTime;
                await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

                if (EventBus != null)
                {
                    await EventBus.PublishAsync(new SubscriptionRenewedEvent
                    {
                        SubscriptionId = subscription.Id,
                        SubscriptionNo = subscription.SubscriptionNo,
                        UserId = subscription.UserId,
                        PlanId = subscription.PlanId,
                        NewEndTime = nextBillingTime,
                        Amount = subscription.Plan.Price,
                        Currency = subscription.Currency,
                        PaymentTradeNo = paymentResult.Data?.TradeNo,
                        AutoRenew = true
                    });
                }

                renewedCount++;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Subscription renewal failed. SubscriptionNo: {SubscriptionNo}",
                    subscription.SubscriptionNo);
            }
        }

        Logger.LogInformation("Renewed {Count} subscriptions out of {Total} due", renewedCount, dueSubscriptions.Count);
        return Ok(renewedCount);
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
