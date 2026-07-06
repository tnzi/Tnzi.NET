namespace Tnzi.Chat;

[DependsOn(typeof(EFCoreModule), typeof(IdentityModule))]
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
        var services = context.Services;

        services.AddScoped<IChatConfigService, ChatConfigService>();
        services.AddScoped<IChatContactService, ChatContactService>();
        services.AddScoped<IPresenceService, PresenceService>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<IBroadcastService, BroadcastService>();
        services.AddScoped<IChatAdminService, ChatAdminService>();

        services.AddEventHandler<ConversationMessageSentEvent, ChatSignalREventHandler>();
        services.AddEventHandler<ConversationReadEvent, ChatSignalREventHandler>();
        services.AddEventHandler<ConversationChangedEvent, ChatSignalREventHandler>();
        services.AddEventHandler<UserConnectedEvent, PresenceConnectionEventHandler>();
        services.AddEventHandler<UserDisconnectedEvent, PresenceConnectionEventHandler>();

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
            webApp.MapHub<ChatHub>("/hubs/chat");
        }

        return Task.CompletedTask;
    }
}
