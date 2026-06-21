namespace Tnzi.Notification;

/// <summary>
/// 通知模块
/// 负责消息发送和记录（邮件、短信、推送）
/// 模板管理由 Template 模块提供，Notification 仅作为消费者
/// 配置路径：Notification
/// </summary>
[DependsOn(typeof(EFCoreModule))]
[OptionalDependsOn(typeof(TemplateModule))]
public class NotificationModule : TnziApplicationModule
{
    /// <summary>
    /// 通知模块加载顺序
    /// </summary>
    public override int LoadOrder => 40;

    /// <summary>
    /// 表名前缀
    /// </summary>
    public override string? TableNamePrefix => "Notification";

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册配置选项
        context.Services.AddTnziOptions<NotificationOptions, NotificationOptionsValidator>(context.Configuration);

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册通知服务（拆分后的 5 个服务）
        context.Services.AddScoped<INotificationService, NotificationService>();
        context.Services.AddScoped<INotificationQueryService, NotificationQueryService>();
        context.Services.AddScoped<INotificationRetryService, NotificationRetryService>();
        context.Services.AddScoped<IUserNotificationService, UserNotificationService>();
        context.Services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();

        // 注册邮件发送服务（使用工厂模式延迟解析配置）
        context.Services.TryAddScoped<IEmailSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
            if (options.MailSender != null)
            {
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                var logger = sp.GetRequiredService<ILogger<MailKitEmailSender>>();
                return new MailKitEmailSender(options, httpClientFactory, logger);
            }
            var nullLogger = sp.GetRequiredService<ILogger<NullEmailSender>>();
            return new NullEmailSender(nullLogger);
        });

        // 注册短信发送服务（使用工厂模式延迟解析配置）
        context.Services.TryAddScoped<ISmsSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
            if (options.SmsSender != null)
            {
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                var logger = sp.GetRequiredService<ILogger<HttpSmsSender>>();
                return new HttpSmsSender(options, httpClientFactory, logger);
            }
            var nullLogger = sp.GetRequiredService<ILogger<NullSmsSender>>();
            return new NullSmsSender(nullLogger);
        });

        // 注册推送通知服务（使用工厂模式延迟解析配置）
        context.Services.TryAddScoped<IPushSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
            if (options.PushSender != null)
            {
                var logger = sp.GetRequiredService<ILogger<PushSender>>();
                return new PushSender(options, logger);
            }
            var nullLogger = sp.GetRequiredService<ILogger<NullPushSender>>();
            return new NullPushSender(nullLogger);
        });

        // 确保HttpClientFactory已注册（用于SMS和Email发送）
        if (!context.Services.Any(s => s.ServiceType == typeof(IHttpClientFactory)))
        {
            context.Services.AddHttpClient();
        }

        // 注册后台队列服务
        context.Services.AddSingleton<ChannelQueueService>();
        context.Services.AddHostedService(sp => sp.GetRequiredService<ChannelQueueService>());
        context.Services.AddSingleton<INotificationQueueService>(sp => sp.GetRequiredService<ChannelQueueService>());

        return Task.CompletedTask;
    }
}

