using Mapster;
using MapsterMapper;
using Tnzi.Chat.Mappings;
using Tnzi.Mapster;

namespace Tnzi.Chat.Tests.Mappings;

public class ChatMappingTests
{
    public ChatMappingTests()
    {
        var config = new TypeAdapterConfig();
        new ChatMappingConfig().Configure(config);
        MapperExtensions.SetMapper(new Mapper(config));
    }

    [Fact]
    public void Should_Map_ChatMessage_To_Dto()
    {
        var m = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            SenderId = Guid.NewGuid(),
            ContentType = MessageContentType.Text,
            Content = "hello",
            SentAt = DateTime.UtcNow
        };
        var dto = m.MapTo<ChatMessageDto>();
        dto.Id.ShouldBe(m.Id);
        dto.Content.ShouldBe("hello");
        dto.ContentType.ShouldBe(MessageContentType.Text);
    }
}
