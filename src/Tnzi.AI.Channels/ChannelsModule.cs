namespace Tnzi.AI.Channels;

/// <summary>
/// IM Channel Bridge 模块 — 将 AI Agent 连接到 Telegram/Feishu/DingTalk 等 IM 平台
/// </summary>
[DependsOn(typeof(AIModule))]
public class ChannelsModule : TnziApplicationModule
{
    public override int LoadOrder => 58;
    public override string? TableNamePrefix => "AI";

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddTnziOptions<ChannelsModuleOptions, ChannelsModuleOptionsValidator>(context.Configuration, "AI:Channels");
        context.Services.AddTnziOptions<GatewayOptions, GatewayOptionsValidator>(context.Configuration, "AI:Channels:Gateway");

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var options = context.Configuration.GetSection("AI:Channels").Get<ChannelsModuleOptions>() ?? new();
        if (!options.Enabled) return Task.CompletedTask;

        var services = context.Services;

        // 消息总线（Singleton — 进程内 Channel<T> 队列）
        services.AddSingleton<IChannelMessageBus, InMemoryChannelMessageBus>();

        // 线程映射存储
        if (string.Equals(options.ThreadStore, "File", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IChannelThreadStore>(sp =>
                new FileChannelThreadStore(sp.GetRequiredService<ILogger<FileChannelThreadStore>>(), options.FileStorePath));
        }
        else
        {
            services.AddScoped<IChannelThreadStore, DatabaseChannelThreadStore>();
        }

        // 消息调度管理器
        services.AddSingleton<IChannelManager, ChannelManager>();

        // Telegram 适配器
        if (options.Telegram.Enabled)
        {
            services.AddSingleton<IChannelAdapter, TelegramChannelAdapter>();
        }

        // Feishu 适配器（同时暴露为 IInboundWebhookAdapter 供 Webhook 控制器使用，单例同实例）
        if (options.Feishu.Enabled)
        {
            services.AddSingleton<FeishuChannelAdapter>();
            services.AddSingleton<IChannelAdapter>(sp => sp.GetRequiredService<FeishuChannelAdapter>());
            services.AddSingleton<IInboundWebhookAdapter>(sp => sp.GetRequiredService<FeishuChannelAdapter>());
        }

        // Slack 适配器
        if (options.Slack.Enabled)
        {
            services.AddHttpClient("Tnzi.AI.Slack");
            services.AddSingleton<SlackChannelAdapter>();
            services.AddSingleton<IChannelAdapter>(sp => sp.GetRequiredService<SlackChannelAdapter>());
            services.AddSingleton<IInboundWebhookAdapter>(sp => sp.GetRequiredService<SlackChannelAdapter>());
        }

        // Discord 适配器
        if (options.Discord.Enabled)
        {
            services.AddHttpClient("Tnzi.AI.Discord");
            services.AddSingleton<DiscordChannelAdapter>();
            services.AddSingleton<IChannelAdapter>(sp => sp.GetRequiredService<DiscordChannelAdapter>());
            services.AddSingleton<IInboundWebhookAdapter>(sp => sp.GetRequiredService<DiscordChannelAdapter>());
        }

        // 钉钉适配器
        if (options.Dingtalk.Enabled)
        {
            services.AddHttpClient("Tnzi.AI.Dingtalk");
            services.AddSingleton<DingtalkChannelAdapter>();
            services.AddSingleton<IChannelAdapter>(sp => sp.GetRequiredService<DingtalkChannelAdapter>());
            services.AddSingleton<IInboundWebhookAdapter>(sp => sp.GetRequiredService<DingtalkChannelAdapter>());
        }

        // HostedService — 绑定 Manager + Adapters 生命周期
        services.AddHostedService<ChannelManagerHostedService>();

        // --- Gateway 服务注册 ---
        var gatewayOptions = context.Configuration.GetSection("AI:Channels:Gateway").Get<GatewayOptions>() ?? new();
        if (gatewayOptions.Enabled)
        {

            // 从配置构建绑定规则
            var bindingRules = gatewayOptions.BindingRules?.Select(r => new SessionBindingRule
            {
                Channel = r.Channel,
                PeerKind = r.PeerKind,
                PeerId = r.PeerId,
                AgentId = r.AgentId,
                Scope = r.Scope,
                Priority = r.Priority,
                IsEnabled = true
            }).ToList() ?? [];

            services.AddSingleton<IReadOnlyList<SessionBindingRule>>(bindingRules.AsReadOnly());
            services.AddSingleton<ISessionBinder, DefaultSessionBinder>();
            services.AddSingleton<IPresenceTracker, DefaultPresenceTracker>();
            services.AddSingleton<IGateway, DefaultGateway>();
        }

        return Task.CompletedTask;
    }

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var app = context.App;
        if (app == null) return Task.CompletedTask;

        var options = context.ServiceProvider.GetRequiredService<IOptions<ChannelsModuleOptions>>().Value;
        if (options.Gateway.Enabled)
        {
            // 当 Gateway 接受匿名连接时，发出醒目的启动告警（生产环境应启用认证）
            if (!options.Gateway.RequireAuthentication)
            {
                context.ServiceProvider.GetRequiredService<ILogger<ChannelsModule>>().LogWarning(
                    "Gateway accepts anonymous WebSocket connections; set AI:Channels:Gateway:RequireAuthentication=true for production");
            }

            app.UseGatewayWebSocket(options.Gateway.Path);
        }

        return Task.CompletedTask;
    }
}
