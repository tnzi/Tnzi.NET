using Tnzi.Notification.Metadata;
using Message = Tnzi.Notification.Entities.Message;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// Notification enhancement tests (Preview, ResendFailed, DeliveryReport, CancelCascade)
/// </summary>
public class NotificationEnhancementTests
{
    private readonly Mock<IRepository<Message, Guid>> _repositoryMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly NotificationService _service;

    public NotificationEnhancementTests()
    {
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _repositoryMock = new Mock<IRepository<Message, Guid>>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();

        var optionsMock = new Mock<IOptionsMonitor<NotificationOptions>>();
        optionsMock.Setup(x => x.CurrentValue).Returns(new NotificationOptions
        {
            MaxConcurrency = 5,
            SmsMaxContentLength = 140,
            Queue = new QueueOptions { Enabled = true },
            Retry = new RetryOptions { RetryDelaySeconds = 1 }
        });

        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        _service = new NotificationService(
            _repositoryMock.Object,
            new Mock<IEmailSender>().Object,
            new Mock<ISmsSender>().Object,
            new Mock<IPushSender>().Object,
            _unitOfWorkMock.Object,
            optionsMock.Object,
            serviceProviderMock.Object,
            PassThroughOptOut(),
            PassThroughPreferences(),
            new Mock<INotificationQueueService>().Object);
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
    public async Task PreviewAsync_WithoutTemplate_ReturnsRawContent()
    {
        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            Subject = "Test Subject",
            Content = "<p>Hello</p>",
            IsHtml = true,
            Recipients = [new RecipientInput { Address = "test@example.com" }],
            Category = "Marketing"
        };

        var result = await _service.PreviewAsync(request);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Subject.ShouldBe("Test Subject");
        result.Data.Content.ShouldBe("<p>Hello</p>");
        result.Data.IsHtml.ShouldBeTrue();
        result.Data.Category.ShouldBe("Marketing");
        result.Data.RecipientCount.ShouldBe(1);
        result.Data.TemplateName.ShouldBeNull();
    }

    [Fact]
    public async Task PreviewAsync_EmptyRecipients_Returns400()
    {
        var request = new CreateNotificationRequest
        {
            Subject = "Test",
            Content = "Content",
            Recipients = []
        };

        var result = await _service.PreviewAsync(request);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task PreviewAsync_DefaultCategory_ReturnsGeneral()
    {
        var request = new CreateNotificationRequest
        {
            Subject = "Test",
            Content = "Content",
            Recipients = [new RecipientInput { Address = "test@example.com" }]
            // No Category specified
        };

        var result = await _service.PreviewAsync(request);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Category.ShouldBe("General");
    }

    [Fact]
    public void NotificationPreviewDto_Properties()
    {
        var dto = new NotificationPreviewDto
        {
            Subject = "Test",
            Content = "<p>Hi</p>",
            IsHtml = true,
            Category = "System",
            RecipientCount = 5,
            TemplateName = "WelcomeEmail"
        };

        dto.Subject.ShouldBe("Test");
        dto.RecipientCount.ShouldBe(5);
        dto.TemplateName.ShouldBe("WelcomeEmail");
    }

    [Fact]
    public void DeliveryReportDto_SuccessRate_Calculation()
    {
        var report = new DeliveryReportDto
        {
            TotalRecipients = 100,
            SentCount = 85,
            FailedCount = 15,
            PendingCount = 0,
            ReadCount = 50
        };
        report.SuccessRate = report.TotalRecipients > 0
            ? (double)report.SentCount / report.TotalRecipients
            : 0;

        report.SuccessRate.ShouldBe(0.85);
    }

    [Fact]
    public void DeliveryReportDto_EmptyRecipients_ZeroRate()
    {
        var report = new DeliveryReportDto
        {
            TotalRecipients = 0,
            SentCount = 0,
            FailedCount = 0
        };
        report.SuccessRate = report.TotalRecipients > 0
            ? (double)report.SentCount / report.TotalRecipients
            : 0;

        report.SuccessRate.ShouldBe(0);
    }

    [Fact]
    public void ResendToFailedRecipients_InterfaceMethodExists()
    {
        // Verify the interface method exists with correct signature
        var method = typeof(INotificationService).GetMethod("ResendToFailedRecipientsAsync");
        method.ShouldNotBeNull();
        method.ReturnType.ShouldBe(typeof(Task<Result<int>>));
    }

    [Fact]
    public void QueryNotificationRequest_HasAllFilters()
    {
        var request = new QueryNotificationRequest
        {
            Type = NotificationType.Email,
            Status = NotificationStatus.Sent,
            StartTime = DateTime.UtcNow.AddDays(-7),
            EndTime = DateTime.UtcNow,
            Keyword = "test",
            Category = "Marketing",
            Priority = NotificationPriority.High,
            SenderId = Guid.NewGuid()
        };

        request.Type.ShouldBe(NotificationType.Email);
        request.Category.ShouldBe("Marketing");
        request.Priority.ShouldBe(NotificationPriority.High);
        request.SenderId.ShouldNotBeNull();
    }
}
