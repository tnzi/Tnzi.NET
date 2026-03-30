using Tnzi.AI.Channels.Entities;
using Tnzi.AI.Channels.Entities.Configs;

namespace Tnzi.Tests.AI.Channels;

public class ChannelThreadMappingConfigurationTests
{
    [Fact]
    public void Entity_HasExpectedProperties()
    {
        var entity = new ChannelThreadMapping
        {
            ChannelName = "telegram",
            ChatId = "123456",
            TopicId = "topic1",
            ThreadId = Guid.NewGuid(),
            ChannelUserId = "user42",
            TenantId = Guid.NewGuid()
        };

        Assert.Equal("telegram", entity.ChannelName);
        Assert.Equal("123456", entity.ChatId);
        Assert.Equal("topic1", entity.TopicId);
        Assert.NotEqual(Guid.Empty, entity.ThreadId);
        Assert.Equal("user42", entity.ChannelUserId);
        Assert.NotNull(entity.TenantId);
    }

    [Fact]
    public void Entity_ImplementsIMultiTenant()
    {
        var entity = new ChannelThreadMapping();
        Assert.IsAssignableFrom<Tnzi.Domain.Entities.IMultiTenant>(entity);
    }

    [Fact]
    public void Entity_InheritsCreationAuditedEntity()
    {
        var entity = new ChannelThreadMapping();
        Assert.IsAssignableFrom<Tnzi.Domain.Entities.CreationAuditedEntity<Guid>>(entity);
    }

    [Fact]
    public void Configuration_InheritsEntityTypeConfigurationBase()
    {
        var config = new ChannelThreadMappingConfiguration();
        Assert.IsAssignableFrom<Tnzi.EFCore.EntityTypeConfigurationBase<ChannelThreadMapping, Guid>>(config);
    }
}
