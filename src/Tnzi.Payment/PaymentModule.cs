namespace Tnzi.Payment;

/// <summary>
/// 支付模块
/// 负责支付处理、退款管理、订阅计费、发票管理、促销优惠等功能
/// 配置路径：Payment
/// </summary>
[DependsOn(typeof(EFCoreModule))]
[DependsOn(typeof(EventBusModule))]
[DependsOn(typeof(CachingModule))]
[OptionalDependsOn(typeof(TemplateModule))]
[OptionalDependsOn(typeof(NotificationModule))]
public class PaymentModule : TnziApplicationModule
{
    /// <summary>
    /// 支付模块加载顺序
    /// </summary>
    public override int LoadOrder => 50;

    /// <summary>
    /// 表名前缀
    /// </summary>
    public override string? TableNamePrefix => "Payment";

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var configuration = context.Configuration;

        // 注册配置选项
        context.Services.Configure<PaymentOptions>(configuration.GetSection("Payment"));
        // 支付渠道配置（独立 Options 类）
        context.Services.Configure<StripeOptions>(configuration.GetSection("Payment:Stripe"));
        context.Services.Configure<PayPalOptions>(configuration.GetSection("Payment:PayPal"));
        // 子模块配置 — 虽为 PaymentOptions 嵌套属性，但服务中单独注入 IOptions<T>
        context.Services.Configure<InvoiceOptions>(configuration.GetSection("Payment:Invoice"));
        context.Services.Configure<PromotionOptions>(configuration.GetSection("Payment:Promotion"));
        context.Services.Configure<TaxOptions>(configuration.GetSection("Payment:Tax"));

        // 注册配置验证器
        context.Services.AddSingleton<IValidateOptions<PaymentOptions>, PaymentOptionsValidator>();
        context.Services.AddSingleton<IValidateOptions<StripeOptions>, StripeOptionsValidator>();
        context.Services.AddSingleton<IValidateOptions<PayPalOptions>, PayPalOptionsValidator>();

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册服务
        context.Services.AddScoped<IPaymentService, PaymentService>();
        context.Services.AddScoped<IRefundService, RefundService>();
        context.Services.AddScoped<ISubscriptionService, SubscriptionService>();
        context.Services.AddScoped<IInvoiceService, InvoiceService>();
        context.Services.AddScoped<IPromotionService, PromotionService>();
        context.Services.AddScoped<ICouponService, CouponService>();
        context.Services.AddScoped<IPaymentStatisticsService, PaymentStatisticsService>();

        // 注册支付渠道
        context.Services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();
        context.Services.AddScoped<IPaymentProvider, StripeProvider>();
        context.Services.AddScoped<IPaymentProvider, PayPalProvider>();
        context.Services.AddScoped<IPaymentProvider, NullProvider>();

        // 注册后台任务（过期支付关闭 + 订阅到期续费）
        context.Services.AddHostedService<PaymentBackgroundService>();

        // 注册事件处理器
        context.Services.AddEventHandler<PaymentCompletedEvent, PaymentCompletedEventHandler>();
        context.Services.AddEventHandler<PaymentFailedEvent, PaymentFailedEventHandler>();
        context.Services.AddEventHandler<PaymentExpiredEvent, PaymentExpiredEventHandler>();
        context.Services.AddEventHandler<RefundProcessedEvent, RefundProcessedEventHandler>();
        context.Services.AddEventHandler<SubscriptionCreatedEvent, SubscriptionCreatedEventHandler>();
        context.Services.AddEventHandler<SubscriptionCancelledEvent, SubscriptionCancelledEventHandler>();
        context.Services.AddEventHandler<SubscriptionExpiredEvent, SubscriptionExpiredEventHandler>();
        context.Services.AddEventHandler<SubscriptionRenewedEvent, SubscriptionRenewedEventHandler>();
        context.Services.AddEventHandler<SubscriptionPlanChangedEvent, SubscriptionPlanChangedEventHandler>();
        context.Services.AddEventHandler<SubscriptionTrialConvertedEvent, SubscriptionTrialConvertedEventHandler>();

        return Task.CompletedTask;
    }

    public override Task PostConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 确保 HttpClientFactory 已注册（PayPal Provider 依赖）
        if (!context.Services.Any(s => s.ServiceType == typeof(IHttpClientFactory)))
        {
            context.Services.AddHttpClient();
        }

        return Task.CompletedTask;
    }

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        return Task.CompletedTask;
    }
}
