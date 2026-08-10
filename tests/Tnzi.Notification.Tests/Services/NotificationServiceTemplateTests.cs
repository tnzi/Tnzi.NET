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
    private readonly Mock<IOptionsMonitor<NotificationOptions>> _optionsMock;
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
        _optionsMock = new Mock<IOptionsMonitor<NotificationOptions>>();
        _templateRenderServiceMock = new Mock<ITemplateRenderService>();

        _optionsMock.Setup(x => x.CurrentValue).Returns(new NotificationOptions
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
            PassThroughOptOut(),
            PassThroughPreferences(),
            null,
            _templateRenderServiceMock.Object
        );
    }

    /// <summary>
    /// 放行一切的退订服务替身。退订本身的行为由 <c>OptOutEnforcementTests</c> 覆盖；
    /// 这里只是让其余用例保持"没有人退订过"这个前提。
    /// ★ 必须显式配置：未配置的 mock 会返回空集合，那等于"所有人都退订了"，
    /// 会让一批与退订无关的用例以极其费解的方式变红。
    /// </summary>
    private static INotificationOptOutService PassThroughOptOut()
    {
        var mock = new Mock<INotificationOptOutService>();
        mock.Setup(x => x.FilterAllowedAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<NotificationType>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> addresses, NotificationType _, string? _, CancellationToken _)
                => addresses.ToList());
        return mock.Object;
    }

    /// <summary>
    /// 放行一切的偏好服务替身。偏好本身的行为由 <c>PreferenceEnforcementTests</c> 覆盖；
    /// 这些用例关心的是别的事，不该因为多了一道过滤而改变结论。
    /// </summary>
    private static INotificationPreferenceService PassThroughPreferences()
    {
        var mock = new Mock<INotificationPreferenceService>();
        mock.Setup(p => p.FilterEnabledUsersAsync(
                It.IsAny<IEnumerable<Guid>>(), It.IsAny<NotificationType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Guid> ids, NotificationType _, string? __, CancellationToken ___) => ids.ToList());
        return mock.Object;
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

    [Theory]
    [InlineData(NotificationType.Sms, "Sms")]
    [InlineData(NotificationType.Email, "Email")]
    public async Task CreateAsync_WithTemplate_NoCategory_Defaults_LookupCategory_To_Channel(
        NotificationType type, string expectedCategory)
    {
        // Regression: framework notification templates ship under channel
        // subdirs (Templates/Notification/{Email|Sms}/TwoFactorCode.cshtml) so
        // the lookup Category must be the channel. The 2FA/welcome/reset event
        // handlers set Type but not Category; without the channel default the
        // lookup used a flat path, missed the file, and sent an empty body.
        _templateRenderServiceMock
            .Setup(x => x.RenderByNameAsync("TwoFactorCode", "Notification", It.IsAny<object?>(), expectedCategory, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RenderedTemplate>.Success(new RenderedTemplate
            {
                Subject = string.Empty,
                Content = "Your code is 123456",
                TemplateName = "TwoFactorCode"
            }));

        var request = new CreateNotificationRequest
        {
            Type = type,
            TemplateName = "TwoFactorCode",
            // Category deliberately omitted, mirroring the framework handlers.
            TemplateVariables = new Dictionary<string, object> { ["Code"] = "123456" },
            Recipients = new List<RecipientInput>
            {
                new RecipientInput { Address = "recipient@example.com", Name = "TestUser" }
            }
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert - body rendered (non-empty) because the channel-derived
        // category resolved the template.
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Content.ShouldBe("Your code is 123456");
        _templateRenderServiceMock.Verify(
            x => x.RenderByNameAsync("TwoFactorCode", "Notification", It.IsAny<object?>(), expectedCategory, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
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
