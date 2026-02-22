
namespace Tnzi.Chat.Tests.Integration;

/// <summary>
/// Chat 模块集成测试
/// </summary>
public class ChatIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task Should_Create_And_Query_Message()
    {
        // Arrange
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Title = "Test Message",
            Content = "Test Content",
            MessageType = MessageType.Public,
            SenderId = Guid.NewGuid(),
            IsSent = false,
            CanReply = true,
            CreationTime = DateTime.UtcNow
        };

        // Act
        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();

        // Assert
        var savedMessage = await DbContext.Messages.FirstOrDefaultAsync(m => m.Title == "Test Message");
        savedMessage.ShouldNotBeNull();
        savedMessage!.Content.ShouldBe("Test Content");
        savedMessage.MessageType.ShouldBe(MessageType.Public);
    }

    [Fact]
    public async Task Should_Create_Private_Message_With_Recipients()
    {
        // Arrange
        var senderId = Guid.NewGuid();
        var recipientId1 = Guid.NewGuid();
        var recipientId2 = Guid.NewGuid();

        var message = new Message
        {
            Id = Guid.NewGuid(),
            Title = "Private Message",
            Content = "Private Content",
            MessageType = MessageType.Private,
            SenderId = senderId,
            IsSent = true,
            CreationTime = DateTime.UtcNow
        };

        // Act
        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();

        // 使用关系表添加收件人
        await DbContext.Set<MessageRecipient>().AddRangeAsync(
            new MessageRecipient { Id = Guid.NewGuid(), MessageId = message.Id, UserId = recipientId1 },
            new MessageRecipient { Id = Guid.NewGuid(), MessageId = message.Id, UserId = recipientId2 }
        );
        await DbContext.SaveChangesAsync();

        // Assert
        var savedMessage = await DbContext.Messages.FirstOrDefaultAsync(m => m.SenderId == senderId);
        savedMessage.ShouldNotBeNull();
        savedMessage!.MessageType.ShouldBe(MessageType.Private);

        var recipients = await DbContext.Set<MessageRecipient>()
            .Where(r => r.MessageId == message.Id)
            .ToListAsync();
        recipients.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Should_Update_Message()
    {
        // Arrange
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Title = "Original Title",
            Content = "Original Content",
            MessageType = MessageType.Public,
            SenderId = Guid.NewGuid(),
            IsSent = false,
            CreationTime = DateTime.UtcNow
        };
        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();

        // Act
        message.Title = "Updated Title";
        message.Content = "Updated Content";
        message.IsSent = true;
        await DbContext.SaveChangesAsync();

        // Assert
        var updatedMessage = await DbContext.Messages.FindAsync(message.Id);
        updatedMessage.ShouldNotBeNull();
        updatedMessage!.Title.ShouldBe("Updated Title");
        updatedMessage.IsSent.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_Delete_Message()
    {
        // Arrange
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Title = "Delete Test",
            Content = "Content",
            MessageType = MessageType.Public,
            SenderId = Guid.NewGuid(),
            CreationTime = DateTime.UtcNow
        };
        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();

        // Act
        DbContext.Messages.Remove(message);
        await DbContext.SaveChangesAsync();

        // Assert - Message 支持软删除
        var deletedMessage = await DbContext.Messages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == message.Id);

        deletedMessage.ShouldNotBeNull();
        deletedMessage!.IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_Query_Messages_By_Type()
    {
        // Arrange
        await DbContext.Messages.AddAsync(new Message
        {
            Id = Guid.NewGuid(),
            Title = "Public Message",
            Content = "Content",
            MessageType = MessageType.Public,
            SenderId = Guid.NewGuid(),
            CreationTime = DateTime.UtcNow
        });

        await DbContext.Messages.AddAsync(new Message
        {
            Id = Guid.NewGuid(),
            Title = "Private Message",
            Content = "Content",
            MessageType = MessageType.Private,
            SenderId = Guid.NewGuid(),
            CreationTime = DateTime.UtcNow
        });
        await DbContext.SaveChangesAsync();

        // Act
        var publicMessages = await DbContext.Messages
            .Where(m => m.MessageType == MessageType.Public)
            .ToListAsync();

        // Assert
        publicMessages.Count.ShouldBe(1);
        publicMessages.First().Title.ShouldBe("Public Message");
    }

    [Fact]
    public async Task Should_Query_Messages_By_Sender()
    {
        // Arrange
        var senderId = Guid.NewGuid();
        for (int i = 1; i <= 3; i++)
        {
            await DbContext.Messages.AddAsync(new Message
            {
                Id = Guid.NewGuid(),
                Title = $"Message {i}",
                Content = $"Content {i}",
                MessageType = MessageType.Public,
                SenderId = senderId,
                CreationTime = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await DbContext.SaveChangesAsync();

        // Act
        var senderMessages = await DbContext.Messages
            .Where(m => m.SenderId == senderId)
            .OrderByDescending(m => m.CreationTime)
            .ToListAsync();

        // Assert
        senderMessages.Count.ShouldBe(3);
        senderMessages.First().Title.ShouldBe("Message 1");
    }

    [Fact]
    public async Task Should_Support_Paging()
    {
        // Arrange
        for (int i = 1; i <= 15; i++)
        {
            await DbContext.Messages.AddAsync(new Message
            {
                Id = Guid.NewGuid(),
                Title = $"Message {i}",
                Content = $"Content {i}",
                MessageType = MessageType.Public,
                SenderId = Guid.NewGuid(),
                CreationTime = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await DbContext.SaveChangesAsync();

        // Act
        var page1 = await DbContext.Messages
            .OrderByDescending(m => m.CreationTime)
            .Skip(0)
            .Take(10)
            .ToListAsync();

        var page2 = await DbContext.Messages
            .OrderByDescending(m => m.CreationTime)
            .Skip(10)
            .Take(10)
            .ToListAsync();

        // Assert
        page1.Count.ShouldBe(10);
        page2.Count.ShouldBe(5);
        page1.First().Title.ShouldBe("Message 1");
    }

    [Fact]
    public async Task Should_Track_Replies_Via_Navigation()
    {
        // Arrange
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Title = "Message with Replies",
            Content = "Content",
            MessageType = MessageType.Public,
            SenderId = Guid.NewGuid(),
            CreationTime = DateTime.UtcNow
        };
        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();

        // Act - 通过导航属性关联回复
        var userId = Guid.NewGuid();
        for (int i = 0; i < 3; i++)
        {
            await DbContext.Set<MessageReply>().AddAsync(new MessageReply
            {
                Id = Guid.NewGuid(),
                Content = $"Reply {i + 1}",
                UserId = userId,
                BelongMessageId = message.Id,
                CreationTime = DateTime.UtcNow
            });
        }
        await DbContext.SaveChangesAsync();

        // Assert - 通过导航属性验证回复数量
        var messageWithReplies = await DbContext.Messages
            .Include(m => m.Replies)
            .FirstOrDefaultAsync(m => m.Id == message.Id);
        messageWithReplies.ShouldNotBeNull();
        messageWithReplies!.Replies.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Should_Create_Message_With_Role_Targets()
    {
        // Arrange
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Title = "Public Announcement",
            Content = "Content for roles",
            MessageType = MessageType.Public,
            SenderId = Guid.NewGuid(),
            IsSent = true,
            CreationTime = DateTime.UtcNow
        };
        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();

        var roleId1 = Guid.NewGuid();
        var roleId2 = Guid.NewGuid();
        await DbContext.Set<MessageRole>().AddRangeAsync(
            new MessageRole { Id = Guid.NewGuid(), MessageId = message.Id, RoleId = roleId1 },
            new MessageRole { Id = Guid.NewGuid(), MessageId = message.Id, RoleId = roleId2 }
        );
        await DbContext.SaveChangesAsync();

        // Assert
        var roles = await DbContext.Set<MessageRole>()
            .Where(r => r.MessageId == message.Id)
            .ToListAsync();
        roles.Count.ShouldBe(2);
    }
}
