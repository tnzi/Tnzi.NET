
using Tnzi.Notification.Metadata;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// NotificationService 单元测试
/// </summary>
public class NotificationServiceTests
{
    private readonly Mock<IRepository<Message, Guid>> _repositoryMock;
    private readonly Mock<IEmailSender> _emailSenderMock;
    private readonly Mock<ISmsSender> _smsSenderMock;
    private readonly Mock<IPushSender> _pushSenderMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IOptions<NotificationOptions>> _optionsMock;
    private readonly Mock<INotificationQueueService> _queueServiceMock;
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        // Initialize Mapster
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _repositoryMock = new Mock<IRepository<Message, Guid>>();
        _emailSenderMock = new Mock<IEmailSender>();
        _smsSenderMock = new Mock<ISmsSender>();
        _pushSenderMock = new Mock<IPushSender>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _optionsMock = new Mock<IOptions<NotificationOptions>>();
        _queueServiceMock = new Mock<INotificationQueueService>();

        _optionsMock.Setup(x => x.Value).Returns(new NotificationOptions
        {
            MaxConcurrency = 5,
            SmsMaxContentLength = 140,
            Queue = new QueueOptions { Enabled = true },
            Retry = new RetryOptions { RetryDelaySeconds = 1 }
        });

        // 设置 IServiceProvider mock 以提供 ILoggerFactory（ApplicationService.Logger 需要）
        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        _service = new NotificationService(
            _repositoryMock.Object,
            _emailSenderMock.Object,
            _smsSenderMock.Object,
            _pushSenderMock.Object,
            _unitOfWorkMock.Object,
            _optionsMock.Object,
            serviceProviderMock.Object,
            _queueServiceMock.Object
        );
    }

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_Should_Create_Message_Success()
    {
        // Arrange
        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            Subject = "Test Subject",
            Content = "Test Content",
            Recipients = new List<RecipientInput>
            {
                new RecipientInput { Address = "test@example.com", Name = "TestUser" }
            }
        };

        _repositoryMock.Setup(r => r.InsertAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .Callback<Message, CancellationToken>((msg, ct) =>
            {
                // 模拟数据库自动生成 ID
                if (msg.Id == Guid.Empty)
                {
                    msg.Id = Guid.NewGuid();
                }
            })
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Id.ShouldNotBe(Guid.Empty);
        result.Data.Subject.ShouldBe("Test Subject");
        result.Data.Content.ShouldBe("Test Content");
        result.Data.Status.ShouldBe(NotificationStatus.Pending);
        result.Data.Recipients.Count.ShouldBe(1);
        result.Data.Recipients.First().Address.ShouldBe("test@example.com");
        _repositoryMock.Verify(r => r.InsertAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_Should_Return_Failure_When_Recipients_Empty()
    {
        // Arrange
        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            Subject = "Test",
            Content = "Test",
            Recipients = new List<RecipientInput>() // Empty
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Succeeded.ShouldBeFalse();
        (result.Message ?? string.Empty).ShouldContain("At least one recipient is required");
    }

    [Fact]
    public async Task CreateAsync_Should_Create_Multiple_Recipients()
    {
        // Arrange
        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            Subject = "Test",
            Content = "Test",
            Recipients = new List<RecipientInput>
            {
                new RecipientInput { Address = "user1@example.com", Name = "User1" },
                new RecipientInput { Address = "user2@example.com", Name = "User2" },
                new RecipientInput { Address = "user3@example.com", Name = "User3" }
            }
        };

        _repositoryMock.Setup(r => r.InsertAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.Recipients.Count.ShouldBe(3);
        result.Data!.TotalRecipientCount.ShouldBe(3);
    }

    [Fact]
    public async Task CreateAsync_Should_Return_Failure_When_Recipient_Address_Empty()
    {
        // Arrange
        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            Subject = "Test",
            Content = "Test",
            Recipients = new List<RecipientInput>
            {
                new RecipientInput { Address = "", Name = "User1" }
            }
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Succeeded.ShouldBeFalse();
        (result.Message ?? string.Empty).ShouldContain("empty address");
    }

    [Fact]
    public async Task CreateAsync_Should_Save_Changes_After_Insert()
    {
        // Arrange
        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            Subject = "Test",
            Content = "Test",
            Recipients = new List<RecipientInput>
            {
                new RecipientInput { Address = "test@example.com", Name = "TestUser" }
            }
        };

        _repositoryMock.Setup(r => r.InsertAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _service.CreateAsync(request);

        // Assert
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}
