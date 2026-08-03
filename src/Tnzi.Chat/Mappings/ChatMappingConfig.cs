using Mapster;
using Tnzi.Mapping;

namespace Tnzi.Chat.Mappings;

public class ChatMappingConfig : IMappingConfig
{
    // Framework-facing: called by MapsterModule at startup via IMappingConfigContext
    public void Configure(IMappingConfigContext context)
    {
        // ChatMessage -> ChatMessageDto: same-name properties map automatically; SenderName is filled by the service layer.
        context.NewConfig<ChatMessage, ChatMessageDto>()
            .Ignore(d => d.SenderName!);
    }

    // Convenience overload: accepts a raw TypeAdapterConfig so callers (e.g. unit tests) can configure
    // mappings without needing the internal MapsterMappingConfigContext adapter.
    public void Configure(TypeAdapterConfig config)
    {
        config.NewConfig<ChatMessage, ChatMessageDto>()
            .Ignore(d => d.SenderName!);
    }
}
