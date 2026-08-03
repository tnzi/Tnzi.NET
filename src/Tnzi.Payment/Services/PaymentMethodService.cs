namespace Tnzi.Payment.Services;

/// <summary>
/// 已保存支付方式（绑卡）服务实现
/// </summary>
public class PaymentMethodService : ApplicationService, IPaymentMethodService
{
    private readonly IRepository<StoredPaymentMethod, Guid> _methodRepository;
    private readonly IRepository<Subscription, Guid> _subscriptionRepository;
    private readonly IPaymentProviderFactory _paymentProviderFactory;
    private readonly IOptionsMonitor<PaymentOptions> _paymentOptionsMonitor;

    public PaymentMethodService(
        IRepository<StoredPaymentMethod, Guid> methodRepository,
        IRepository<Subscription, Guid> subscriptionRepository,
        IPaymentProviderFactory paymentProviderFactory,
        IOptionsMonitor<PaymentOptions> paymentOptionsMonitor,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _methodRepository = Check.NotNull(methodRepository);
        _subscriptionRepository = Check.NotNull(subscriptionRepository);
        _paymentProviderFactory = Check.NotNull(paymentProviderFactory);
        _paymentOptionsMonitor = Check.NotNull(paymentOptionsMonitor);
    }

    public async Task<Result<SetupSessionDto>> CreateSetupSessionAsync(Guid userId, CreateSetupSessionDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var channelCode = ResolveChannelCode(request.ChannelCode);
        var provider = _paymentProviderFactory.GetProvider(channelCode);
        if (provider == null)
            return Fail<SetupSessionDto>(ErrorCodes.PaymentChannelNotSupported, 400);

        if (!provider.SupportsPaymentMethodStorage)
            return Fail<SetupSessionDto>(ErrorCodes.PaymentMethodStorageNotSupported, 400);

        // 复用该用户在该渠道下已有的渠道客户，避免每次绑卡都在渠道侧新建一个客户
        var providerCustomerId = await FindProviderCustomerIdAsync(userId, channelCode, cancellationToken);

        var result = await provider.CreateSetupSessionAsync(new PaymentProviderSetupDto
        {
            UserId = userId,
            ProviderCustomerId = providerCustomerId,
            CustomerName = CurrentUser?.UserName,
            CustomerEmail = CurrentUser?.Email,
            ReturnUrl = request.ReturnUrl,
            CancelUrl = request.CancelUrl
        });

        if (!result.Succeeded || result.Data == null)
            return Fail<SetupSessionDto>(result.Message ?? ErrorCodes.PaymentMethodBindingFailed, result.Code ?? 400);

        return Ok(new SetupSessionDto
        {
            ChannelCode = channelCode,
            SetupId = result.Data.SetupId,
            ClientSecret = result.Data.ClientSecret,
            ApprovalUrl = result.Data.ApprovalUrl
        });
    }

    public async Task<Result<StoredPaymentMethodDto>> BindAsync(Guid userId, BindPaymentMethodDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var result = await BindEntityAsync(
            userId,
            ResolveChannelCode(request.ChannelCode),
            request.PaymentMethodToken,
            CurrentUser?.UserName,
            CurrentUser?.Email,
            request.SetAsDefault,
            cancellationToken);

        if (!result.Succeeded || result.Data == null)
            return Fail<StoredPaymentMethodDto>(result.Message ?? ErrorCodes.PaymentMethodBindingFailed, result.Code ?? 400);

        return Ok(ToDto(result.Data));
    }

    public async Task<Result<StoredPaymentMethod>> BindEntityAsync(
        Guid userId,
        string channelCode,
        string paymentMethodToken,
        string? customerName,
        string? customerEmail,
        bool setAsDefault,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodToken))
            return Fail<StoredPaymentMethod>(ErrorCodes.PaymentMethodNotFound, 400);

        var provider = _paymentProviderFactory.GetProvider(channelCode);
        if (provider == null)
            return Fail<StoredPaymentMethod>(ErrorCodes.PaymentChannelNotSupported, 400);

        if (!provider.SupportsPaymentMethodStorage)
            return Fail<StoredPaymentMethod>(ErrorCodes.PaymentMethodStorageNotSupported, 400);

        var providerCustomerId = await FindProviderCustomerIdAsync(userId, channelCode, cancellationToken);

        // 向渠道校验：确认支付方式存在、归属本用户的渠道客户（必要时补挂），并取回展示信息。
        // 不做这一步就落库，等于把一个可能无效的 token 留到扣款当天才炸。
        var resolved = await provider.ResolvePaymentMethodAsync(new PaymentProviderResolveMethodDto
        {
            PaymentMethodToken = paymentMethodToken,
            ProviderCustomerId = providerCustomerId,
            UserId = userId,
            CustomerName = customerName,
            CustomerEmail = customerEmail
        });

        if (!resolved.Succeeded || resolved.Data == null)
            return Fail<StoredPaymentMethod>(resolved.Message ?? ErrorCodes.PaymentMethodBindingFailed, resolved.Code ?? 400);

        var descriptor = resolved.Data;

        var stored = await ExecuteInUnitOfWorkAsync(async ct =>
        {
            // ClearDefaultAsync 是裸 SQL：物理事务延迟开启，不先强开它会在自动提交模式执行，
            // 于是"清掉旧默认"落库而"设置新默认"随异常回滚 → 用户一个默认支付方式都没有
            await _methodRepository.EnsureTransactionStartedAsync(ct);

            var existing = await _methodRepository.FirstOrDefaultAsync(
                m => m.ChannelCode == channelCode && m.Token == descriptor.Token, ct);

            if (existing != null && existing.UserId != userId)
            {
                // 同一 token 已归属他人：拒绝，避免把别人的卡挂到当前用户名下
                Logger.LogWarning("Payment method token already bound to another user. Channel: {Channel}", channelCode);
                return Fail<StoredPaymentMethod>(ErrorCodes.PaymentMethodBindingFailed, 409);
            }

            var isFirstMethod = !await _methodRepository.AnyAsync(
                m => m.UserId == userId && m.ChannelCode == channelCode && m.IsActive, ct);

            // 首个支付方式必须是默认的，否则后台扣款找不到默认卡
            var shouldBeDefault = setAsDefault || isFirstMethod;

            if (shouldBeDefault)
                await ClearDefaultAsync(userId, channelCode, ct);

            if (existing != null)
            {
                existing.ProviderCustomerId = descriptor.ProviderCustomerId ?? existing.ProviderCustomerId;
                existing.MethodType = descriptor.MethodType;
                existing.Brand = descriptor.Brand;
                existing.Last4 = descriptor.Last4;
                existing.AccountLabel = descriptor.AccountLabel;
                existing.ExpiryMonth = descriptor.ExpiryMonth;
                existing.ExpiryYear = descriptor.ExpiryYear;
                existing.IsActive = true;
                existing.IsDefault = shouldBeDefault || existing.IsDefault;
                await _methodRepository.UpdateAsync(existing, ct);
                return Ok(existing);
            }

            var created = new StoredPaymentMethod
            {
                UserId = userId,
                ChannelCode = channelCode,
                ProviderCustomerId = descriptor.ProviderCustomerId,
                Token = descriptor.Token,
                MethodType = descriptor.MethodType,
                Brand = descriptor.Brand,
                Last4 = descriptor.Last4,
                AccountLabel = descriptor.AccountLabel,
                ExpiryMonth = descriptor.ExpiryMonth,
                ExpiryYear = descriptor.ExpiryYear,
                IsDefault = shouldBeDefault,
                IsActive = true
            };

            await _methodRepository.InsertAsync(created, ct);
            return Ok(created);
        }, cancellationToken);

        if (!stored.Succeeded || stored.Data == null)
            return stored;

        Logger.LogInformation("Payment method bound. UserId: {UserId}, Channel: {Channel}, Brand: {Brand}, Last4: {Last4}",
            userId, channelCode, stored.Data.Brand, stored.Data.Last4);

        // 新绑的默认卡同步到该用户尚未绑卡的订阅，让"绑了卡就能自动续费"成立
        if (stored.Data.IsDefault)
            await SyncToUnboundSubscriptionsAsync(userId, stored.Data, cancellationToken);

        return stored;
    }

    public async Task<Result<List<StoredPaymentMethodDto>>> GetUserMethodsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var methods = await _methodRepository.AsNoTracking()
            .Where(m => m.UserId == userId && m.IsActive)
            .OrderByDescending(m => m.IsDefault)
            .ThenByDescending(m => m.CreationTime)
            .ToListAsync(cancellationToken);

        return Ok(methods.Select(ToDto).ToList());
    }

    public async Task<Result> SetDefaultAsync(Guid userId, Guid paymentMethodId, CancellationToken cancellationToken = default)
    {
        var method = await _methodRepository.FirstOrDefaultAsync(
            m => m.Id == paymentMethodId && m.UserId == userId && m.IsActive, cancellationToken);
        if (method == null)
            return Fail(ErrorCodes.PaymentMethodNotFound, 404);

        if (method.IsDefault)
            return Ok();

        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            // 同 BindEntityAsync：裸 SQL 必须先强开物理事务，否则"清旧默认"与"设新默认"可能只落一半
            await _methodRepository.EnsureTransactionStartedAsync(ct);
            await ClearDefaultAsync(userId, method.ChannelCode, ct);
            method.IsDefault = true;
            await _methodRepository.UpdateAsync(method, ct);
            return Ok();
        }, cancellationToken);

        await SyncToUnboundSubscriptionsAsync(userId, method, cancellationToken);

        Logger.LogInformation("Default payment method changed. UserId: {UserId}, MethodId: {MethodId}", userId, paymentMethodId);
        return Ok();
    }

    public async Task<Result> RemoveAsync(Guid userId, Guid paymentMethodId, CancellationToken cancellationToken = default)
    {
        var method = await _methodRepository.FirstOrDefaultAsync(
            m => m.Id == paymentMethodId && m.UserId == userId && m.IsActive, cancellationToken);
        if (method == null)
            return Fail(ErrorCodes.PaymentMethodNotFound, 404);

        var provider = _paymentProviderFactory.GetProvider(method.ChannelCode);
        if (provider != null && provider.SupportsPaymentMethodStorage)
        {
            var detach = await provider.DetachPaymentMethodAsync(new PaymentProviderResolveMethodDto
            {
                PaymentMethodToken = method.Token,
                ProviderCustomerId = method.ProviderCustomerId,
                UserId = userId
            });

            if (!detach.Succeeded)
                return Fail(detach.Message ?? ErrorCodes.PaymentMethodBindingFailed, detach.Code ?? 400);
        }

        // 保留记录只置失效：历史扣款需要溯源到具体卡，物理删除会让对账断链
        method.IsActive = false;
        method.IsDefault = false;
        await _methodRepository.UpdateAsync(method, cancellationToken);

        // 清掉引用该卡的订阅快照，否则后台会拿一个已解绑的 token 反复扣款失败
        await ClearSubscriptionBindingAsync(paymentMethodId, cancellationToken);

        Logger.LogInformation("Payment method removed. UserId: {UserId}, MethodId: {MethodId}", userId, paymentMethodId);
        return Ok();
    }

    public Task<StoredPaymentMethod?> FindDefaultAsync(Guid userId, string channelCode, CancellationToken cancellationToken = default)
    {
        return _methodRepository.FirstOrDefaultAsync(
            m => m.UserId == userId && m.ChannelCode == channelCode && m.IsActive && m.IsDefault, cancellationToken);
    }

    public Task<StoredPaymentMethod?> FindByIdAsync(Guid userId, Guid paymentMethodId, CancellationToken cancellationToken = default)
    {
        return _methodRepository.FirstOrDefaultAsync(
            m => m.Id == paymentMethodId && m.UserId == userId && m.IsActive, cancellationToken);
    }

    public async Task MarkUsedAsync(Guid paymentMethodId, CancellationToken cancellationToken = default)
    {
        await _methodRepository.AsQueryable()
            .Where(m => m.Id == paymentMethodId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastUsedTime, DateTime.UtcNow), cancellationToken);
    }

    public async Task<Result> DeactivateByTokenAsync(string channelCode, string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelCode) || string.IsNullOrWhiteSpace(token))
            return Ok();

        var method = await _methodRepository.FirstOrDefaultAsync(
            m => m.ChannelCode == channelCode && m.Token == token, cancellationToken);

        // 找不到 / 已失效都直接成功：渠道会重投同一事件，这条路径必须是幂等的；
        // 而且这个凭据也可能压根不是本系统绑的
        if (method == null || !method.IsActive)
            return Ok();

        var affected = await ExecuteInUnitOfWorkAsync(async ct =>
        {
            // ClearSubscriptionBindingAsync 是裸 SQL：物理事务延迟开启，不先强开它会在自动提交模式执行，
            // 于是"清订阅快照"落库而"置失效"随异常回滚——订阅没了卡，支付方式却还显示可用
            await _methodRepository.EnsureTransactionStartedAsync(ct);

            method.IsActive = false;
            method.IsDefault = false;
            await _methodRepository.UpdateAsync(method, ct);

            // 不清掉订阅上的快照，后台会拿一个已经作废的凭据反复扣款失败
            var count = await ClearSubscriptionBindingAsync(method.Id, ct);
            return Ok(count);
        }, cancellationToken);

        Logger.LogWarning(
            "Payment method revoked at the channel. UserId: {UserId}, Channel: {Channel}, MethodId: {MethodId}, AffectedSubscriptions: {Count}",
            method.UserId, channelCode, method.Id, affected.Data);

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new PaymentMethodRevokedEvent
            {
                PaymentMethodId = method.Id,
                UserId = method.UserId,
                ChannelCode = channelCode,
                Brand = method.Brand,
                Last4 = method.Last4,
                AffectedSubscriptionCount = affected.Data
            });
        }

        return Ok();
    }

    private string ResolveChannelCode(string? channelCode)
        => string.IsNullOrWhiteSpace(channelCode)
            ? _paymentOptionsMonitor.CurrentValue.DefaultChannelCode
            : channelCode;

    private async Task<string?> FindProviderCustomerIdAsync(Guid userId, string channelCode, CancellationToken cancellationToken)
    {
        return await _methodRepository.AsNoTracking()
            .Where(m => m.UserId == userId && m.ChannelCode == channelCode && m.ProviderCustomerId != null)
            .OrderByDescending(m => m.IsActive)
            .ThenByDescending(m => m.CreationTime)
            .Select(m => m.ProviderCustomerId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task ClearDefaultAsync(Guid userId, string channelCode, CancellationToken cancellationToken)
    {
        return _methodRepository.AsQueryable()
            .Where(m => m.UserId == userId && m.ChannelCode == channelCode && m.IsDefault)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsDefault, false), cancellationToken);
    }

    /// <summary>
    /// 把新绑定的默认支付方式同步到该用户尚未绑卡的有效订阅。
    /// 已显式绑过其它卡的订阅不动，避免覆盖用户的明确选择。
    /// </summary>
    private async Task SyncToUnboundSubscriptionsAsync(Guid userId, StoredPaymentMethod method, CancellationToken cancellationToken)
    {
        var affected = await _subscriptionRepository.AsQueryable()
            .Where(s => s.UserId == userId
                && s.ChannelCode == method.ChannelCode
                && s.StoredPaymentMethodId == null
                && s.Status != SubscriptionStatus.Cancelled
                && s.Status != SubscriptionStatus.Expired)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.StoredPaymentMethodId, method.Id)
                .SetProperty(x => x.PaymentMethodToken, method.Token)
                .SetProperty(x => x.ProviderCustomerId, method.ProviderCustomerId)
                .SetProperty(x => x.PaymentMethodBrand, method.Brand)
                .SetProperty(x => x.PaymentMethodLast4, method.Last4), cancellationToken);

        if (affected > 0)
            Logger.LogInformation("Bound payment method to {Count} subscriptions without one. UserId: {UserId}", affected, userId);
    }

    /// <summary>
    /// 清掉引用该支付方式的订阅快照，返回受影响的订阅数。
    /// </summary>
    private Task<int> ClearSubscriptionBindingAsync(Guid paymentMethodId, CancellationToken cancellationToken)
    {
        return _subscriptionRepository.AsQueryable()
            .Where(s => s.StoredPaymentMethodId == paymentMethodId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.StoredPaymentMethodId, (Guid?)null)
                .SetProperty(x => x.PaymentMethodToken, (string?)null)
                .SetProperty(x => x.PaymentMethodBrand, (string?)null)
                .SetProperty(x => x.PaymentMethodLast4, (string?)null), cancellationToken);
    }

    private static StoredPaymentMethodDto ToDto(StoredPaymentMethod method) => new()
    {
        Id = method.Id,
        ChannelCode = method.ChannelCode,
        MethodType = method.MethodType,
        Brand = method.Brand,
        Last4 = method.Last4,
        AccountLabel = method.AccountLabel,
        ExpiryMonth = method.ExpiryMonth,
        ExpiryYear = method.ExpiryYear,
        IsDefault = method.IsDefault,
        IsExpired = method.IsExpired(DateTime.UtcNow),
        LastUsedTime = method.LastUsedTime,
        CreationTime = method.CreationTime
    };
}
