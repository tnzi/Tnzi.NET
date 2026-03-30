namespace Tnzi.Payment.Events.Handlers;

/// <summary>
/// 支付完成事件处理器
/// 自动生成发票并发送
/// </summary>
public class PaymentCompletedEventHandler : IEventHandler<PaymentCompletedEvent>
{
    private readonly ILogger<PaymentCompletedEventHandler> _logger;
    private readonly IInvoiceService? _invoiceService;
    private readonly IOptions<InvoiceOptions>? _invoiceOptions;

    public PaymentCompletedEventHandler(
        ILogger<PaymentCompletedEventHandler> logger,
        IInvoiceService? invoiceService = null,
        IOptions<InvoiceOptions>? invoiceOptions = null)
    {
        _logger = Check.NotNull(logger);
        _invoiceService = invoiceService;
        _invoiceOptions = invoiceOptions;
    }

    public async Task HandleAsync(PaymentCompletedEvent eventData, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Payment completed. TradeNo: {TradeNo}, Amount: {Amount} {Currency}",
                eventData.TradeNo, eventData.Amount, eventData.Currency);

            // 自动生成发票
            if (_invoiceService != null
                && _invoiceOptions?.Value is { Enabled: true, AutoSendOnPayment: true })
            {
                var invoiceResult = await _invoiceService.CreateFromPaymentAsync(
                    eventData.PaymentId, null, cancellationToken);

                if (invoiceResult.Succeeded && invoiceResult.Data != null)
                {
                    _logger.LogInformation(
                        "Invoice auto-created from payment. InvoiceId: {InvoiceId}, TradeNo: {TradeNo}",
                        invoiceResult.Data.Id, eventData.TradeNo);

                    // 自动发送发票
                    await _invoiceService.SendAsync(invoiceResult.Data.Id, null, null, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            // 事件处理器错误不影响主业务流程，静默忽略
            _logger.LogWarning(ex, "Failed to handle payment completed event. TradeNo: {TradeNo}", eventData.TradeNo);
        }
    }
}

/// <summary>
/// 支付失败事件处理器
/// 记录支付失败日志
/// </summary>
public class PaymentFailedEventHandler : IEventHandler<PaymentFailedEvent>
{
    private readonly ILogger<PaymentFailedEventHandler> _logger;

    public PaymentFailedEventHandler(ILogger<PaymentFailedEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(PaymentFailedEvent eventData, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogWarning(
                "Payment failed. TradeNo: {TradeNo}, ErrorCode: {ErrorCode}, Reason: {Reason}",
                eventData.TradeNo, eventData.ErrorCode, eventData.FailReason);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle payment failed event. TradeNo: {TradeNo}", eventData.TradeNo);
        }
    }
}

/// <summary>
/// 退款处理完成事件处理器
/// 记录退款结果日志
/// </summary>
public class RefundProcessedEventHandler : IEventHandler<RefundProcessedEvent>
{
    private readonly ILogger<RefundProcessedEventHandler> _logger;

    public RefundProcessedEventHandler(ILogger<RefundProcessedEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(RefundProcessedEvent eventData, CancellationToken cancellationToken = default)
    {
        try
        {
            if (eventData.Succeeded)
            {
                _logger.LogInformation(
                    "Refund succeeded. RefundNo: {RefundNo}, Amount: {Amount} {Currency}",
                    eventData.RefundNo, eventData.Amount, eventData.Currency);
            }
            else
            {
                _logger.LogWarning(
                    "Refund failed. RefundNo: {RefundNo}, Reason: {Reason}",
                    eventData.RefundNo, eventData.FailReason);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle refund processed event. RefundNo: {RefundNo}", eventData.RefundNo);
        }
    }
}

/// <summary>
/// 支付过期事件处理器
/// 记录支付过期日志
/// </summary>
public class PaymentExpiredEventHandler : IEventHandler<PaymentExpiredEvent>
{
    private readonly ILogger<PaymentExpiredEventHandler> _logger;

    public PaymentExpiredEventHandler(ILogger<PaymentExpiredEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(PaymentExpiredEvent eventData, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Payment expired. TradeNo: {TradeNo}, BusinessOrderNo: {BusinessOrderNo}, ExpiredTime: {ExpiredTime}",
                eventData.TradeNo, eventData.BusinessOrderNo, eventData.ExpiredTime);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle payment expired event. TradeNo: {TradeNo}", eventData.TradeNo);
        }
    }
}

/// <summary>
/// 订阅创建事件处理器
/// 记录订阅创建日志
/// </summary>
public class SubscriptionCreatedEventHandler : IEventHandler<SubscriptionCreatedEvent>
{
    private readonly ILogger<SubscriptionCreatedEventHandler> _logger;

    public SubscriptionCreatedEventHandler(ILogger<SubscriptionCreatedEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(SubscriptionCreatedEvent eventData, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Subscription created. SubscriptionNo: {SubscriptionNo}, UserId: {UserId}, Plan: {PlanName}, IsTrial: {IsTrial}",
                eventData.SubscriptionNo, eventData.UserId, eventData.PlanName, eventData.IsTrial);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle subscription created event. SubscriptionNo: {SubscriptionNo}", eventData.SubscriptionNo);
        }
    }
}

/// <summary>
/// 订阅取消事件处理器
/// 记录订阅取消日志
/// </summary>
public class SubscriptionCancelledEventHandler : IEventHandler<SubscriptionCancelledEvent>
{
    private readonly ILogger<SubscriptionCancelledEventHandler> _logger;

    public SubscriptionCancelledEventHandler(ILogger<SubscriptionCancelledEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(SubscriptionCancelledEvent eventData, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Subscription cancelled. SubscriptionNo: {SubscriptionNo}, UserId: {UserId}, Immediate: {Immediate}, Reason: {Reason}",
                eventData.SubscriptionNo, eventData.UserId, eventData.Immediate, eventData.CancelReason);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle subscription cancelled event. SubscriptionNo: {SubscriptionNo}", eventData.SubscriptionNo);
        }
    }
}

/// <summary>
/// 订阅过期事件处理器
/// 记录订阅过期日志
/// </summary>
public class SubscriptionExpiredEventHandler : IEventHandler<SubscriptionExpiredEvent>
{
    private readonly ILogger<SubscriptionExpiredEventHandler> _logger;

    public SubscriptionExpiredEventHandler(ILogger<SubscriptionExpiredEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(SubscriptionExpiredEvent eventData, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Subscription expired. SubscriptionNo: {SubscriptionNo}, UserId: {UserId}, ExpiredTime: {ExpiredTime}",
                eventData.SubscriptionNo, eventData.UserId, eventData.ExpiredTime);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle subscription expired event. SubscriptionNo: {SubscriptionNo}", eventData.SubscriptionNo);
        }
    }
}

/// <summary>
/// 订阅续费事件处理器
/// 记录订阅续费日志
/// </summary>
public class SubscriptionRenewedEventHandler : IEventHandler<SubscriptionRenewedEvent>
{
    private readonly ILogger<SubscriptionRenewedEventHandler> _logger;

    public SubscriptionRenewedEventHandler(ILogger<SubscriptionRenewedEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(SubscriptionRenewedEvent eventData, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Subscription renewed. SubscriptionNo: {SubscriptionNo}, UserId: {UserId}, NewEndTime: {NewEndTime}, AutoRenew: {AutoRenew}",
                eventData.SubscriptionNo, eventData.UserId, eventData.NewEndTime, eventData.AutoRenew);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle subscription renewed event. SubscriptionNo: {SubscriptionNo}", eventData.SubscriptionNo);
        }
    }
}

/// <summary>
/// 订阅计划变更事件处理器
/// 记录计划变更日志
/// </summary>
public class SubscriptionPlanChangedEventHandler : IEventHandler<SubscriptionPlanChangedEvent>
{
    private readonly ILogger<SubscriptionPlanChangedEventHandler> _logger;

    public SubscriptionPlanChangedEventHandler(ILogger<SubscriptionPlanChangedEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(SubscriptionPlanChangedEvent eventData, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Subscription plan changed. SubscriptionNo: {SubscriptionNo}, UserId: {UserId}, FromPlan: {FromPlanId}, ToPlan: {ToPlanId}, ChangeType: {ChangeType}, ProratedAmount: {ProratedAmount}, Immediate: {Immediate}",
                eventData.SubscriptionNo, eventData.UserId, eventData.FromPlanId, eventData.ToPlanId,
                eventData.ChangeType, eventData.ProratedAmount, eventData.Immediate);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle subscription plan changed event. SubscriptionNo: {SubscriptionNo}", eventData.SubscriptionNo);
        }
    }
}

/// <summary>
/// 试用转正事件处理器
/// 记录试用转正日志
/// </summary>
public class SubscriptionTrialConvertedEventHandler : IEventHandler<SubscriptionTrialConvertedEvent>
{
    private readonly ILogger<SubscriptionTrialConvertedEventHandler> _logger;

    public SubscriptionTrialConvertedEventHandler(ILogger<SubscriptionTrialConvertedEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(SubscriptionTrialConvertedEvent eventData, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Subscription trial converted. SubscriptionNo: {SubscriptionNo}, UserId: {UserId}, ConvertedTime: {ConvertedTime}",
                eventData.SubscriptionNo, eventData.UserId, eventData.ConvertedTime);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle subscription trial converted event. SubscriptionNo: {SubscriptionNo}", eventData.SubscriptionNo);
        }
    }
}
