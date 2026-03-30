namespace Tnzi.Chat;

/// <summary>
/// 聊天模块（站内消息与公告，非 IM 即时通讯）
/// 支持私人消息、公共消息（角色推送）、消息回复、已读追踪
/// </summary>
[DependsOn(typeof(EFCoreModule), typeof(IdentityModule))]
[OptionalDependsOn(typeof(SignalRModule))]
public class ChatModule : TnziApplicationModule
{
    /// <summary>
    /// 表名前缀
    /// </summary>
    public override string? TableNamePrefix => "Chat";

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // 注册消息服务
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IMessageReplyService, MessageReplyService>();
        services.AddScoped<IDraftMessageService, DraftMessageService>();

        // 注册事件处理器
        services.AddEventHandler<MessageSentEvent, ChatSignalREventHandler>();
        services.AddEventHandler<MessageReadEvent, ChatSignalREventHandler>();
        services.AddEventHandler<MessageRepliedEvent, ChatSignalREventHandler>();

        return Task.CompletedTask;
    }
}
