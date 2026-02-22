namespace Tnzi.Chat.Mappings;

/// <summary>
/// Chat 模块映射配置
/// </summary>
public class MessageMappingConfig : IMappingConfig
{
    public void Configure(IMappingConfigContext context)
    {
        // CreateMessageDto → Message
        context.NewConfig<CreateMessageDto, Message>()
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.SenderId)
            .Ignore(dest => dest.IsSent)
            .Ignore(dest => dest.CreationTime)
            .Ignore(dest => dest.Recipients)
            .Ignore(dest => dest.Roles);

        // CreateMessageReplyDto → MessageReply
        context.NewConfig<CreateMessageReplyDto, MessageReply>()
            .Map(dest => dest.BelongMessageId, src => src.MessageId)
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.UserId)
            .Ignore(dest => dest.CreationTime);
    }
}
