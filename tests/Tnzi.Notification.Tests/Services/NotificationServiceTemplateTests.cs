using Tnzi.Notification.Metadata;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// NotificationService 模板相关测试
/// </summary>
public class NotificationServiceTemplateTests
{
    private readonly Mock<IRepository<Message, Guid>> _repositoryMock;
    private readonly Mock<IEmailSender> _emailSenderMock;
    private readonly Mock<ISmsSender> _smsSenderMock;
    private readonly Mock<IPushSender> _pushSenderMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IOptions<NotificationOptions>> _optionsMock;
    private readonly Mock<ITemplateRenderService> _templateRenderServiceMock;
    private readonly NotificationService _service;

    public NotificationServiceTemplateTests()
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
        _templateRenderServiceMock = new Mock<ITemplateRenderService>();

        _optionsMock.Setup(x => x.Value).Returns(new NotificationOptions
        {
            MaxConcurrency = 5,
            SmsMaxContentLength = 140,
            Queue = new QueueOptions { Enabled = false },
            Retry = new RetryOptions { RetryDelaySeconds = 1 }
        });

        _repositoryMock.Setup(r => r.InsertAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .Callback<Message, CancellationToken>((msg, ct) =>
            {
                if (msg.Id == Guid.Empty)
                {
                    msg.Id = Guid.NewGuid();
                }
            })
            .Returns(Task.CompletedTask);

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
            null,
            _templateRenderServiceMock.Object
        );
    }

    [Fact]
    public async Task CreateAsync_WithTemplate_Should_Use_Rendered_Subject_And_Content()
    {
        // Arrange
        _templateRenderServiceMock
            .Setup(x => x.RenderByNameAsync("WelcomeEmail", "Notification", It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RenderedTemplate>.Success(new RenderedTemplate
            {
                Subject = "Welcome to Tnzi.NET!",
                Content = "Hello TestUser!",
                TemplateName = "WelcomeEmail"
            }));

        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            TemplateName = "WelcomeEmail",
            TemplateVariables = new Dictionary<string, object>
            {
                ["AppName"] = "Tnzi.NET",
                ["UserName"] = "TestUser"
            },
            Recipients = new List<RecipientInput>
            {
                new RecipientInput { Address = "test@example.com", Name = "TestUser" }
            }
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Subject.ShouldBe("Welcome to Tnzi.NET!");
        result.Data.Content.ShouldBe("Hello TestUser!");
        _templateRenderServiceMock.Verify(x => x.RenderByNameAsync("WelcomeEmail", "Notification", It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithTemplate_Empty_Subject_Should_Fallback_To_Request_Subject()
    {
        // Arrange - 渲染返回空 Subject，应回退使用 request.Subject
        _templateRenderServiceMock
            .Setup(x => x.RenderByNameAsync("WelcomeEmail", "Notification", It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RenderedTemplate>.Success(new RenderedTemplate
            {
                Subject = string.Empty,
                Content = "Hello TestUser!",
                TemplateName = "WelcomeEmail"
            }));

        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            Subject = "Welcome Email",
            TemplateName = "WelcomeEmail",
            TemplateVariables = new Dictionary<string, object>
            {
                ["UserName"] = "TestUser"
            },
            Recipients = new List<RecipientInput>
            {
                new RecipientInput { Address = "test@example.com", Name = "TestUser" }
            }
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert - Subject 为空时回退到 request.Subject
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Subject.ShouldBe("Welcome Email");
        result.Data.Content.ShouldBe("Hello TestUser!");
    }

    [Fact]
    public async Task CreateAsync_WithTemplate_Render_Failure_Should_Fallback_To_Raw_Content()
    {
        // Arrange - 渲染失败，应回退使用原始内容
        _templateRenderServiceMock
            .Setup(x => x.RenderByNameAsync("BadTemplate", "Notification", It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RenderedTemplate>.Failure("Template not found"));

        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            Subject = "Fallback Subject",
            Content = "Fallback Content",
            TemplateName = "BadTemplate",
            TemplateVariables = new Dictionary<string, object>(),
            Recipients = new List<RecipientInput>
            {
                new RecipientInput { Address = "test@example.com", Name = "TestUser" }
            }
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert - 渲染失败时优雅降级到原始内容
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Subject.ShouldBe("Fallback Subject");
        result.Data.Content.ShouldBe("Fallback Content");
    }

    [Fact]
    public async Task CreateAsync_WithTemplate_Uses_Request_Category()
    {
        // Arrange
        _templateRenderServiceMock
            .Setup(x => x.RenderByNameAsync("WelcomeEmail", "Notification", It.IsAny<object?>(), "Marketing", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RenderedTemplate>.Success(new RenderedTemplate
            {
                Subject = "Welcome!",
                Content = "Hello!",
                TemplateName = "WelcomeEmail"
            }));

        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            TemplateName = "WelcomeEmail",
            Category = "Marketing",
            TemplateVariables = new Dictionary<string, object>(),
            Recipients = new List<RecipientInput>
            {
                new RecipientInput { Address = "test@example.com", Name = "TestUser" }
            }
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Category.ShouldBe("Marketing");
    }

    [Fact]
    public async Task CreateAsync_WithoutTemplate_Should_Use_Raw_Content()
    {
        // Arrange - 没有 TemplateName，直接使用请求中的内容
        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            Subject = "Direct Subject",
            Content = "Direct Content",
            Recipients = new List<RecipientInput>
            {
                new RecipientInput { Address = "test@example.com", Name = "TestUser" }
            }
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Subject.ShouldBe("Direct Subject");
        result.Data.Content.ShouldBe("Direct Content");
        // ITemplateRenderService should not be called
        _templateRenderServiceMock.Verify(
            x => x.RenderByNameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithTemplate_Passes_LayoutName()
    {
        // Arrange - 验证 LayoutName 被传递给 ITemplateRenderService
        _templateRenderServiceMock
            .Setup(x => x.RenderByNameAsync("WelcomeEmail", "Notification", It.IsAny<object?>(), It.IsAny<string?>(), "MainLayout", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RenderedTemplate>.Success(new RenderedTemplate
            {
                Subject = "Welcome!",
                Content = "<html>Hello!</html>",
                TemplateName = "WelcomeEmail",
                LayoutName = "MainLayout"
            }));

        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            TemplateName = "WelcomeEmail",
            LayoutName = "MainLayout",
            TemplateVariables = new Dictionary<string, object>(),
            Recipients = new List<RecipientInput>
            {
                new RecipientInput { Address = "test@example.com", Name = "TestUser" }
            }
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Content.ShouldBe("<html>Hello!</html>");
        _templateRenderServiceMock.Verify(
            x => x.RenderByNameAsync("WelcomeEmail", "Notification", It.IsAny<object?>(), It.IsAny<string?>(), "MainLayout", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
