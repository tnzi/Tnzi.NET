using TemplateEntity = Tnzi.Template.Entities.Template;

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
    private readonly Mock<ITemplateStoreService> _templateStoreServiceMock;
    private readonly Mock<ITemplateEngine> _templateEngineMock;
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
        _templateStoreServiceMock = new Mock<ITemplateStoreService>();
        _templateEngineMock = new Mock<ITemplateEngine>();

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
            _templateStoreServiceMock.Object,
            null,
            _templateEngineMock.Object
        );
    }

    [Fact]
    public async Task CreateAsync_WithTemplate_Should_Use_SubjectTemplate_From_Template()
    {
        // Arrange
        var template = new TemplateEntity
        {
            TemplateName = "WelcomeEmail",
            Module = "Notification",
            Category = "Email",
            SubjectTemplate = "Welcome to @Model.AppName!",
            ContentTemplate = "Hello @Model.UserName!"
        };

        _templateStoreServiceMock
            .Setup(x => x.GetTemplateAsync("WelcomeEmail", "Notification", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TemplateEntity>.Success(template));

        _templateEngineMock
            .Setup(x => x.RenderAsync("Welcome to @Model.AppName!", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Welcome to Tnzi.NET!");

        _templateEngineMock
            .Setup(x => x.RenderAsync("Hello @Model.UserName!", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Hello TestUser!");

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
        _templateStoreServiceMock.Verify(x => x.GetTemplateAsync("WelcomeEmail", "Notification", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithTemplate_No_SubjectTemplate_Should_Fallback_To_Request_Subject()
    {
        // Arrange - 模板没有 SubjectTemplate，应回退使用 request.Subject
        var template = new TemplateEntity
        {
            TemplateName = "WelcomeEmail",
            Module = "Notification",
            Category = "Email",
            SubjectTemplate = string.Empty,
            ContentTemplate = "Hello @Model.UserName!"
        };

        _templateStoreServiceMock
            .Setup(x => x.GetTemplateAsync("WelcomeEmail", "Notification", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TemplateEntity>.Success(template));

        _templateEngineMock
            .Setup(x => x.RenderAsync("Hello @Model.UserName!", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Hello TestUser!");

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

        // Assert - SubjectTemplate 为空时回退到 request.Subject
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Subject.ShouldBe("Welcome Email");
        result.Data.Content.ShouldBe("Hello TestUser!");
    }

    [Fact]
    public async Task CreateAsync_WithTemplate_No_ContentTemplate_Should_Render_Empty_Content()
    {
        // Arrange - 模板 ContentTemplate 为空，渲染后内容也为空但不会失败
        var template = new TemplateEntity
        {
            TemplateName = "WelcomeEmail",
            Module = "Notification",
            Category = "Email",
            SubjectTemplate = "Welcome to @Model.AppName!",
            ContentTemplate = string.Empty
        };

        _templateStoreServiceMock
            .Setup(x => x.GetTemplateAsync("WelcomeEmail", "Notification", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TemplateEntity>.Success(template));

        _templateEngineMock
            .Setup(x => x.RenderAsync("Welcome to @Model.AppName!", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Welcome to Tnzi.NET!");

        _templateEngineMock
            .Setup(x => x.RenderAsync(string.Empty, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            Subject = "Welcome",
            Content = "Hello TestUser!",
            TemplateName = "WelcomeEmail",
            TemplateVariables = new Dictionary<string, object>
            {
                ["AppName"] = "Tnzi.NET"
            },
            Recipients = new List<RecipientInput>
            {
                new RecipientInput { Address = "test@example.com", Name = "TestUser" }
            }
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert - 新服务不校验渲染后空内容，创建成功
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Subject.ShouldBe("Welcome to Tnzi.NET!");
        result.Data.Content.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task CreateAsync_WithTemplate_Not_Found_Should_Return_Failure()
    {
        // Arrange
        _templateStoreServiceMock
            .Setup(x => x.GetTemplateAsync("NonExistent", "Notification", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TemplateEntity>.Failure("Template not found"));

        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            Subject = "Test",
            Content = "Test",
            TemplateName = "NonExistent",
            Recipients = new List<RecipientInput>
            {
                new RecipientInput { Address = "test@example.com", Name = "TestUser" }
            }
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Succeeded.ShouldBeFalse();
        (result.Message ?? string.Empty).ShouldContain("not found");
    }

    [Fact]
    public async Task CreateAsync_WithTemplate_Render_Exception_Should_Return_Failure()
    {
        // Arrange - 模板渲染时抛出异常
        var template = new TemplateEntity
        {
            TemplateName = "BadTemplate",
            Module = "Notification",
            Category = "Email",
            SubjectTemplate = "Welcome!",
            ContentTemplate = "Hello @Model.Invalid!"
        };

        _templateStoreServiceMock
            .Setup(x => x.GetTemplateAsync("BadTemplate", "Notification", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TemplateEntity>.Success(template));

        _templateEngineMock
            .Setup(x => x.RenderAsync("Hello @Model.Invalid!", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Template rendering failed"));

        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            TemplateName = "BadTemplate",
            TemplateVariables = new Dictionary<string, object>(),
            Recipients = new List<RecipientInput>
            {
                new RecipientInput { Address = "test@example.com", Name = "TestUser" }
            }
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Succeeded.ShouldBeFalse();
        (result.Message ?? string.Empty).ShouldContain("Failed to render template");
    }

    [Fact]
    public async Task CreateAsync_WithTemplate_Uses_Template_Category()
    {
        // Arrange - 模板有 Category，应使用模板的 Category
        var template = new TemplateEntity
        {
            TemplateName = "WelcomeEmail",
            Module = "Notification",
            Category = "Marketing",
            SubjectTemplate = "Welcome!",
            ContentTemplate = "Hello!"
        };

        _templateStoreServiceMock
            .Setup(x => x.GetTemplateAsync("WelcomeEmail", "Notification", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TemplateEntity>.Success(template));

        _templateEngineMock
            .Setup(x => x.RenderAsync("Welcome!", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Welcome!");

        _templateEngineMock
            .Setup(x => x.RenderAsync("Hello!", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Hello!");

        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            TemplateName = "WelcomeEmail",
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
}
