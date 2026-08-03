namespace Tnzi.Chat;

[DependsOn(typeof(EFCoreModule), typeof(IdentityModule), typeof(IdentityPresenceModule))]
[OptionalDependsOn(typeof(SignalRModule))]
public class ChatModule : TnziApplicationModule
{
    public override string? TableNamePrefix => "Chat";

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddTnziOptions<ChatOptions, ChatOptionsValidator>(context.Configuration);
        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Code-declared permissions for this module's admin surfaces - the
        // Authorization module's PermissionDbSeeder picks every registered
        // provider up on startup (no-op when Authorization is not loaded).
        context.Services.AddTransient<IPermissionDefinitionProvider, ChatPermissions>();

        var services = context.Services;

        services.AddScoped<IChatAccessService, ChatAccessService>();
        services.AddScoped<ChatAccessGuardFilter>();
        services.AddScoped<IChatConfigService, ChatConfigService>();
        services.AddScoped<IChatContactService, ChatContactService>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<IBroadcastService, BroadcastService>();
        services.AddScoped<IChatAdminService, ChatAdminService>();

        // 让会话成员读得到会话里的文件。Storage 自己只认识"创建者 / storage.file.view",
        // 接收方两样都不是 —— 没有这条,同一张图在发的人那里能看、在收的人那里 404。
        // Storage 未加载时该注册无害:没有人会去解析它。
        services.AddScoped<IFileReferenceAccessResolver, ChatFileReferenceAccessResolver>();

        services.AddEventHandler<ConversationMessageSentEvent, ChatSignalREventHandler>();
        services.AddEventHandler<ConversationReadEvent, ChatSignalREventHandler>();
        services.AddEventHandler<ConversationChangedEvent, ChatSignalREventHandler>();
        // Presence 现由 Tnzi.Identity.Presence 拥有并发布 UserPresenceChangedEvent；
        // Chat 只订阅它，按会话联系人扇出 Chat.PresenceChanged（保留原有行为）。
        services.AddEventHandler<UserPresenceChangedEvent, ChatPresenceRelayHandler>();

        // Wire realtime push only when SignalR is actually loaded (OptionalDependsOn → may be absent).
        var appDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ITnziApplication));
        if (appDescriptor?.ImplementationInstance is ITnziApplication tnziApp
            && tnziApp.IsModuleLoaded<SignalRModule>())
        {
            services.AddScoped<IMessagePushService<ChatHub>, MessagePushService<ChatHub>>();
            services.AddScoped<IMessagePushService>(sp => sp.GetRequiredService<IMessagePushService<ChatHub>>());
        }

        return Task.CompletedTask;
    }

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var webApp = context.WebApp;
        if (webApp == null)
            return Task.CompletedTask;

        // Map the hub only when SignalRModule is loaded.
        var tnziApp = context.ServiceProvider.GetService<ITnziApplication>();
        if (tnziApp != null && tnziApp.IsModuleLoaded<SignalRModule>())
        {
            webApp.MapTnziHub<ChatHub>("chat", "/hubs/chat");
        }

        return Task.CompletedTask;
    }
}
