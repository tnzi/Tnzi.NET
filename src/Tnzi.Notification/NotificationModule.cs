namespace Tnzi.Notification;

/// <summary>
/// 通知模块
/// 负责消息发送和记录（邮件、短信、推送、传真）
/// 模板管理由 Template 模块提供，Notification 仅作为消费者
/// 配置路径：Notification
/// </summary>
[DependsOn(typeof(EFCoreModule), typeof(AspNetCoreModule))]
[OptionalDependsOn(typeof(TemplateModule))]
// IPushSender 的契约与回退都在本模块，实现住在可选的 Tnzi.Notification.Push 里。
// 依赖审计从 DI 图推箭头，看到「本模块用了子模块注册的服务」就要求声明 [DependsOn]，
// 方向恰好是反的：本模块不依赖子模块，子模块依赖本模块。
[SuppressDependencyAudit("Contract and fallback own by this module; the implementation lives in the optional Tnzi.Notification.Push sub-module, so the dependency runs child -> parent", IgnoredServiceType = typeof(IPushSender))]
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
        // Code-declared permissions for this module's admin surfaces - the
        // Authorization module's PermissionDbSeeder picks every registered
        // provider up on startup (no-op when Authorization is not loaded).
        context.Services.AddTransient<IPermissionDefinitionProvider, NotificationPermissions>();

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

        // 注册传真发送服务（使用工厂模式延迟解析配置）
        // ★ 回退实现与上面三条渠道不同：未配置时**失败**而不是报成功，理由见 UnconfiguredFaxSender。
        context.Services.TryAddScoped<IFaxSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
            if (options.FaxSender is { Enabled: true } fax && !string.IsNullOrWhiteSpace(fax.GatewayDomain))
            {
                var emailSender = sp.GetRequiredService<IEmailSender>();
                var logger = sp.GetRequiredService<ILogger<EmailToFaxSender>>();
                return new EmailToFaxSender(options, emailSender, logger);
            }
            var nullLogger = sp.GetRequiredService<ILogger<UnconfiguredFaxSender>>();
            return new UnconfiguredFaxSender(nullLogger);
        });

        // 传真回执：把网关几分钟后回来的那封确认邮件读成"这份传真到没到"。
        // ★ **收件箱没配就整条链都不起**（判据 FaxConfirmationOptions.IsUsable，与配置校验同源）。
        // 判读与落库这两件是纯粹的服务，注册了也不会自己动，所以无条件注册好让 webhook /
        // 人工补录这些别的来源也能用；真正按配置开关的只有那个后台轮询。
        context.Services.TryAddScoped<IFaxConfirmationParser, HeuristicFaxConfirmationParser>();
        context.Services.TryAddScoped<IFaxConfirmationService, FaxConfirmationService>();
        context.Services.TryAddScoped<IFaxConfirmationMailbox, ImapFaxConfirmationMailbox>();

        var faxConfirmation = context.Configuration
            .GetSection("Notification:FaxSender:Confirmation")
            .Get<FaxConfirmationOptions>();

        if (faxConfirmation is { IsUsable: true })
        {
            context.Services.AddHostedService<FaxConfirmationBackgroundService>();
        }

        // 确保HttpClientFactory已注册（用于SMS和Email发送）
        if (!context.Services.Any(s => s.ServiceType == typeof(IHttpClientFactory)))
        {
            context.Services.AddHttpClient();
        }

        // 注册后台队列服务
        context.Services.AddSingleton<ChannelQueueService>();
        context.Services.AddHostedService(sp => sp.GetRequiredService<ChannelQueueService>());
        context.Services.AddSingleton<INotificationQueueService>(sp => sp.GetRequiredService<ChannelQueueService>());

        // 退订（群发合规：CASL / CAN-SPAM 要求一键退订且须很快生效）
        context.Services.AddScoped<INotificationOptOutService, NotificationOptOutService>();

        // 派发恢复：把进程中途退出后停在 Sending 的批次接着发完。
        // 收件人状态本来就逐行持久化，缺的只是把它接回去的那个循环。
        context.Services.AddHostedService<NotificationDispatchBackgroundService>();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 推送渠道的回退实现。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 真正的 FCM 实现住在可选的 <c>Tnzi.Notification.Push</c> 子模块里，把 <c>FirebaseAdmin</c>
    /// 与 5 个 <c>Google.*</c> 传递包挡在只发邮件/短信的消费方的依赖闭包之外。
    /// </para>
    /// <para>
    /// <b>为什么补位放在 PostConfigure 而不是 Configure。</b> 子模块的 <c>LoadOrder</c> 在本模块之后，
    /// 若在 Configure 阶段用 <c>TryAdd</c> 补位，先跑的是本模块，子模块的注册反而被跳过 ——
    /// 服务照样解析得到、调用照样成功，只是永远走不到真正的实现。
    /// PostConfigure 跑在所有模块的 Configure 之后，<c>TryAdd</c> 因此只在真的没人注册时才生效。
    /// </para>
    /// <para>
    /// 两种缺席分得很清楚：没配 <c>Notification:PushSender</c> = 这个部署不发推送，沿用
    /// <see cref="NullPushSender"/>（行为与拆分前一致）；配了却没有实现 = 部署方要发推送但少装了包，
    /// 用 <see cref="UnconfiguredPushSender"/> 当场失败并指名要加载什么，绝不谎报已投递。
    /// </para>
    /// </remarks>
    public override Task PostConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.TryAddScoped<IPushSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
            if (options.PushSender != null)
            {
                var logger = sp.GetRequiredService<ILogger<UnconfiguredPushSender>>();
                return new UnconfiguredPushSender(logger);
            }

            var nullLogger = sp.GetRequiredService<ILogger<NullPushSender>>();
            return new NullPushSender(nullLogger);
        });

        return Task.CompletedTask;
    }
}

