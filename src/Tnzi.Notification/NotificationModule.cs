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

        // 服务商解析（默认 + 具名）与路由策略。两者都 TryAdd：消费方替换选择器是常规操作，
        // 替换解析器不是，但也没有理由禁止。
        context.Services.TryAddScoped<INotificationProviderResolver, NotificationProviderResolver>();
        context.Services.TryAddScoped<INotificationProviderSelector, DefaultNotificationProviderSelector>();

        // 每条渠道：一个默认发送器（普通注册）+ 配置里每个具名节一个 keyed 发送器。
        // 默认发送器用工厂模式延迟解析配置；具名节的键必须在 Configure 阶段就读出来（keyed service 按键登记），
        // 节的内容仍在解析时从绑定好的 NotificationOptions 取。
        RegisterEmailSenders(context);
        RegisterSmsSenders(context);
        RegisterFaxSenders(context);

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

        // 远程附件下载：禁自动重定向。EgressGuard 只审原始 URL，跟随 302 会把第二跳（云元数据 / 内网）
        // 直接发出去并把回应装进信里 —— 见 NotificationHttpClientNames。
        context.Services.AddHttpClient(NotificationHttpClientNames.Attachments)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

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
    /// 分档与邮件 / 短信相同：配了 <c>Notification:PushSender</c> 却没有实现 = 部署方要发推送但少装了包，
    /// 用 <see cref="UnconfiguredPushSender"/> 当场失败并指名要加载什么；只配了具名节 <c>PushSenders</c>
    /// 没配默认节 = 显然要推送，没带键的消息不能落进一个报成功的空实现（同样失败，提示指向配置）；
    /// 什么都没配 = 这个部署不发推送，沿用 <see cref="NullPushSender"/>（行为与拆分前一致）。绝不谎报已投递。
    /// </para>
    /// </remarks>
    public override Task PostConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.TryAddScoped<IPushSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
            if (options.PushSender != null)
                return new UnconfiguredPushSender(sp.GetRequiredService<ILogger<UnconfiguredPushSender>>());

            if (options.PushSenders.Count > 0)
                return UnconfiguredPushSender.ForMissingDefault(sp.GetRequiredService<ILogger<UnconfiguredPushSender>>());

            return new NullPushSender(sp.GetRequiredService<ILogger<NullPushSender>>());
        });

        // 具名推送节：配了就是要发，没有实现时每个键都当场失败并指名要加载什么 —— 与默认节同一分档。
        foreach (var key in NotificationProviderProfiles.NamedKeys(context.Configuration, "PushSenders"))
        {
            context.Services.TryAddKeyedScoped<IPushSender>(key, (sp, _) =>
                new UnconfiguredPushSender(sp.GetRequiredService<ILogger<UnconfiguredPushSender>>()));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 邮件：默认节 → <see cref="MailKitEmailSender"/>；只有具名节没有默认节 → <see cref="UnconfiguredEmailSender"/>
    /// （显然要发信的部署里，没带键的消息不能落进一个报成功的空实现）；什么都没配 → <see cref="NullEmailSender"/>。
    /// </summary>
    private static void RegisterEmailSenders(ServiceConfigurationContext context)
    {
        context.Services.TryAddScoped<IEmailSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
            if (options.MailSender != null)
                return NewMailSender(sp, options.MailSender);

            if (options.MailSenders.Count > 0)
                return new UnconfiguredEmailSender(sp.GetRequiredService<ILogger<UnconfiguredEmailSender>>());

            return new NullEmailSender(sp.GetRequiredService<ILogger<NullEmailSender>>());
        });

        foreach (var key in NotificationProviderProfiles.NamedKeys(context.Configuration, "MailSenders"))
        {
            context.Services.TryAddKeyedScoped<IEmailSender>(key, (sp, _) =>
            {
                var options = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
                return NewMailSender(sp, NotificationProviderProfiles.Get(options.MailSenders, key, "MailSenders"));
            });
        }
    }

    /// <summary>短信：分档与邮件相同。</summary>
    private static void RegisterSmsSenders(ServiceConfigurationContext context)
    {
        context.Services.TryAddScoped<ISmsSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
            if (options.SmsSender != null)
                return NewSmsSender(sp, options.SmsSender);

            if (options.SmsSenders.Count > 0)
                return new UnconfiguredSmsSender(sp.GetRequiredService<ILogger<UnconfiguredSmsSender>>());

            return new NullSmsSender(sp.GetRequiredService<ILogger<NullSmsSender>>());
        });

        foreach (var key in NotificationProviderProfiles.NamedKeys(context.Configuration, "SmsSenders"))
        {
            context.Services.TryAddKeyedScoped<ISmsSender>(key, (sp, _) =>
            {
                var options = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
                return NewSmsSender(sp, NotificationProviderProfiles.Get(options.SmsSenders, key, "SmsSenders"));
            });
        }
    }

    /// <summary>
    /// 传真：★ 回退实现与另外三条渠道不同，未配置时<b>失败</b>而不是报成功，理由见 <see cref="UnconfiguredFaxSender"/>。
    /// 所以这里没有「只有具名节没有默认节」的特殊分档 —— 默认节缺席本来就是失败。
    /// </summary>
    private static void RegisterFaxSenders(ServiceConfigurationContext context)
    {
        context.Services.TryAddScoped<IFaxSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
            return NewFaxSender(sp, options.FaxSender, "Notification:FaxSender");
        });

        foreach (var key in NotificationProviderProfiles.NamedKeys(context.Configuration, "FaxSenders"))
        {
            context.Services.TryAddKeyedScoped<IFaxSender>(key, (sp, _) =>
            {
                var options = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
                var profile = NotificationProviderProfiles.Get(options.FaxSenders, key, "FaxSenders");
                return NewFaxSender(sp, profile, $"Notification:FaxSenders:{key}");
            });
        }
    }

    /// <summary>
    /// 一节邮件配置对应的发送器。附件来源纪律（<c>Notification:Attachments</c>）是全局的一节，
    /// 每个实例都要拿到 —— 漏传的症状是"配了 AllowedLocalRoots 而每个本地附件照样失败"，与没配一模一样。
    /// </summary>
    private static IEmailSender NewMailSender(IServiceProvider sp, MailSenderOptions profile)
        => new MailKitEmailSender(
            profile,
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<MailKitEmailSender>>(),
            sp.GetRequiredService<IOptions<NotificationOptions>>().Value.Attachments);

    private static ISmsSender NewSmsSender(IServiceProvider sp, SmsSenderOptions profile)
        => new HttpSmsSender(profile, sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<ILogger<HttpSmsSender>>());

    /// <summary>
    /// 一节传真配置对应的发送器。承载它的邮件发送器按 <see cref="FaxSenderOptions.EmailProviderKey"/> 经解析器取，
    /// <b>取不到就抛</b>：退回默认邮件发送器发出去的信网关不认，症状是「传真发成功了但永远没到」。
    /// </summary>
    private static IFaxSender NewFaxSender(IServiceProvider sp, FaxSenderOptions? profile, string path)
    {
        if (profile is not { Enabled: true } || string.IsNullOrWhiteSpace(profile.GatewayDomain))
            return new UnconfiguredFaxSender(sp.GetRequiredService<ILogger<UnconfiguredFaxSender>>());

        var carrier = sp.GetRequiredService<INotificationProviderResolver>().Resolve<IEmailSender>(profile.EmailProviderKey)
            ?? throw new ConfigurationException(
                $"{path}:EmailProviderKey",
                $"Mail sender '{NotificationProviderKeys.Normalize(profile.EmailProviderKey) ?? NotificationProviderKeys.Default}' is not registered; "
                + "the fax profile has no mail sender to deliver through. Configure it under Notification:MailSenders or register a keyed IEmailSender with that key.");

        return new EmailToFaxSender(profile, carrier, sp.GetRequiredService<ILogger<EmailToFaxSender>>());
    }
}

