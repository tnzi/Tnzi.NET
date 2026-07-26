namespace Tnzi.Identity.Presence;

/// <summary>
/// 用户在线状态（presence）扩展子模块 —— 独立机制：手动状态意图、连接派生的在线解析、
/// 可配置的 auto-away、通用实时推送（/hubs/presence）。Chat 依赖本模块消费在线状态；
/// 不需要 Chat 的应用也可单独加载它来实时展示用户在线状态。
/// </summary>
[DependsOn(typeof(IdentityModule))]
[OptionalDependsOn(typeof(SignalRModule))]
public class IdentityPresenceModule : TnziApplicationModule
{
    // 共享 Identity 表前缀：Identity_UserPresence（对齐业务子模块共享父前缀先例）。
    public override string? TableNamePrefix => "Identity";

    // Identity(0) 之后、Chat 之前；实际次序由 [DependsOn] 拓扑排序保证，此值仅为同级 tiebreak。
    public override int LoadOrder => 5;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddTnziOptions<PresenceOptions, PresenceOptionsValidator>(context.Configuration);
        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        services.AddScoped<IPresenceService, PresenceService>();
        services.AddScoped<IPresenceConfigService, PresenceConfigService>();

        // 连接事件驱动自动上/下线（无需自定义心跳）。
        services.AddEventHandler<UserConnectedEvent, PresenceConnectionEventHandler>();
        services.AddEventHandler<UserDisconnectedEvent, PresenceConnectionEventHandler>();

        // 通用实时推送只在实际加载了 SignalR 时接线（OptionalDependsOn → 可能缺席）。
        var appDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ITnziApplication));
        if (appDescriptor?.ImplementationInstance is ITnziApplication tnziApp
            && tnziApp.IsModuleLoaded<SignalRModule>())
        {
            services.AddScoped<IMessagePushService<PresenceHub>, MessagePushService<PresenceHub>>();
            services.AddEventHandler<UserPresenceChangedEvent, PresenceRealtimePushHandler>();
        }

        return Task.CompletedTask;
    }

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var webApp = context.WebApp;
        if (webApp == null)
            return Task.CompletedTask;

        // 仅在 SignalRModule 加载时映射通用 presence hub。
        var tnziApp = context.ServiceProvider.GetService<ITnziApplication>();
        if (tnziApp != null && tnziApp.IsModuleLoaded<SignalRModule>())
        {
            webApp.MapHub<PresenceHub>("/hubs/presence");
        }

        return Task.CompletedTask;
    }
}
