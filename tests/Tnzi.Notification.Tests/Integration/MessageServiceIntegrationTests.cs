
using Tnzi.Notification.Metadata;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// Notification 模块集成测试
/// 验证基本的数据库操作和查询功能
/// </summary>
public class NotificationIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task Should_Create_And_Query_Message()
    {
        // Arrange
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Test Subject",
            Content = "Test Content",
            Type = NotificationType.Email,
            Status = NotificationStatus.Pending,
            CreationTime = DateTime.UtcNow
        };

        // Act
        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();

        // Assert
        var savedMessage = await DbContext.Messages.FirstOrDefaultAsync(m => m.Subject == "Test Subject");
        savedMessage.ShouldNotBeNull();
        savedMessage!.Content.ShouldBe("Test Content");
        savedMessage.Type.ShouldBe(NotificationType.Email);
    }

    [Fact]
    public async Task Should_Update_Message_Status()
    {
        // Arrange
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Update Test",
            Content = "Content",
            Type = NotificationType.Sms,
            Status = NotificationStatus.Pending,
            CreationTime = DateTime.UtcNow
        };
        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();

        // Act
        message.Status = NotificationStatus.Sent;
        message.SentTime = DateTime.UtcNow;
        await DbContext.SaveChangesAsync();

        // Assert
        var updatedMessage = await DbContext.Messages.FindAsync(message.Id);
        updatedMessage.ShouldNotBeNull();
        updatedMessage!.Status.ShouldBe(NotificationStatus.Sent);
        updatedMessage.SentTime.ShouldNotBeNull();
    }

    [Fact]
    public async Task Should_Query_Messages_By_Type()
    {
        // Arrange
        await DbContext.Messages.AddAsync(new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Email Message",
            Content = "Content",
            Type = NotificationType.Email,
            Status = NotificationStatus.Pending,
            CreationTime = DateTime.UtcNow
        });

        await DbContext.Messages.AddAsync(new Message
        {
            Id = Guid.NewGuid(),
            Subject = "SMS Message",
            Content = "Content",
            Type = NotificationType.Sms,
            Status = NotificationStatus.Pending,
            CreationTime = DateTime.UtcNow
        });
        await DbContext.SaveChangesAsync();

        // Act
        var emailMessages = await DbContext.Messages
            .Where(m => m.Type == NotificationType.Email)
            .ToListAsync();

        // Assert
        emailMessages.Count.ShouldBe(1);
        emailMessages.First().Subject.ShouldBe("Email Message");
    }

    [Fact]
    public async Task Should_Query_Messages_By_Status()
    {
        // Arrange
        await DbContext.Messages.AddAsync(new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Pending Message",
            Content = "Content",
            Type = NotificationType.Email,
            Status = NotificationStatus.Pending,
            CreationTime = DateTime.UtcNow
        });

        await DbContext.Messages.AddAsync(new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Sent Message",
            Content = "Content",
            Type = NotificationType.Email,
            Status = NotificationStatus.Sent,
            SentTime = DateTime.UtcNow,
            CreationTime = DateTime.UtcNow
        });
        await DbContext.SaveChangesAsync();

        // Act
        var sentMessages = await DbContext.Messages
            .Where(m => m.Status == NotificationStatus.Sent)
            .ToListAsync();

        // Assert
        sentMessages.Count.ShouldBe(1);
        sentMessages.First().Subject.ShouldBe("Sent Message");
    }

    [Fact]
    public async Task Should_Support_Paging()
    {
        // Arrange
        for (int i = 1; i <= 25; i++)
        {
            await DbContext.Messages.AddAsync(new Message
            {
                Id = Guid.NewGuid(),
                Subject = $"Message {i}",
                Content = $"Content {i}",
                Type = NotificationType.Email,
                Status = NotificationStatus.Pending,
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
        page2.Count.ShouldBe(10);
        page1.First().Subject.ShouldBe("Message 1");
    }

    [Fact]
    public async Task Should_Delete_Message()
    {
        // Arrange
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Delete Test",
            Content = "Content",
            Type = NotificationType.Email,
            Status = NotificationStatus.Pending,
            CreationTime = DateTime.UtcNow
        };
        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();

        // Act
        DbContext.Messages.Remove(message);
        await DbContext.SaveChangesAsync();

        // Assert - Message 实体支持软删除,需要使用 IgnoreQueryFilters
        var deletedMessage = await DbContext.Messages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == message.Id);
        
        deletedMessage.ShouldNotBeNull();
        deletedMessage!.IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_Track_Retry_Count()
    {
        // Arrange
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Retry Test",
            Content = "Content",
            Type = NotificationType.Email,
            Status = NotificationStatus.Failed,
            RetryCount = 0,
            MaxRetryCount = 3,
            CreationTime = DateTime.UtcNow
        };
        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();

        // Act
        message.RetryCount++;
        message.Status = NotificationStatus.Pending;
        await DbContext.SaveChangesAsync();

        // Assert
        var updatedMessage = await DbContext.Messages.FindAsync(message.Id);
        updatedMessage.ShouldNotBeNull();
        updatedMessage!.RetryCount.ShouldBe(1);
        updatedMessage.Status.ShouldBe(NotificationStatus.Pending);
    }

    [Fact]
    public async Task Should_Count_Messages()
    {
        // Arrange
        for (int i = 1; i <= 10; i++)
        {
            await DbContext.Messages.AddAsync(new Message
            {
                Id = Guid.NewGuid(),
                Subject = $"Message {i}",
                Content = $"Content {i}",
                Type = NotificationType.Email,
                Status = NotificationStatus.Pending,
                CreationTime = DateTime.UtcNow
            });
        }
        await DbContext.SaveChangesAsync();

        // Act
        var count = await DbContext.Messages.CountAsync();

        // Assert
        count.ShouldBe(10);
    }
}