
namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// MailKitEmailSender 单元测试
/// </summary>
public class MailKitEmailSenderTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<MailKitEmailSender>> _loggerMock;
    private readonly NotificationOptions _options;

    public MailKitEmailSenderTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<MailKitEmailSender>>();

        _options = new NotificationOptions
        {
            MaxConcurrency = 5,
            MailSender = new MailSenderOptions
            {
                FromEmail = "test@example.com",
                FromName = "Test Sender",
                SmtpServer = "smtp.example.com",
                SmtpPort = 587,
                EnableSsl = true,
                Username = "testuser",
                Password = "testpass"
            }
        };
    }

    [Fact]
    public void Constructor_Should_Initialize_Successfully()
    {
        // Act
        var sender = new MailKitEmailSender(_options, _httpClientFactoryMock.Object, _loggerMock.Object);

        // Assert
        sender.ShouldNotBeNull();
    }

    [Fact]
    public async Task SendToAsync_Should_Return_Failure_When_Options_Not_Configured()
    {
        // Arrange
        var optionsWithoutMailSender = new NotificationOptions();
        var sender = new MailKitEmailSender(optionsWithoutMailSender, _httpClientFactoryMock.Object, _loggerMock.Object);

        // Act
        var result = await sender.SendToAsync("test@example.com", "Test", "Subject", "Body");

        // Assert
        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("not configured");
    }

    [Fact]
    public async Task SendAsync_Should_Return_Failure_When_Options_Not_Configured()
    {
        // Arrange
        var optionsWithoutMailSender = new NotificationOptions();
        var sender = new MailKitEmailSender(optionsWithoutMailSender, _httpClientFactoryMock.Object, _loggerMock.Object);

        // Act
        var result = await sender.SendAsync(new EmailMessage
        {
            To = [new EmailAddress("a@example.com")],
            Subject = "Subject",
            Body = "Body"
        });

        // Assert
        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("not configured");
    }

    [Fact]
    public async Task SendAsync_Should_Return_Failure_When_Message_Has_No_Recipient()
    {
        // Arrange - 空信封在连 SMTP 之前就该被挡下，否则失败原因会变成一条难懂的协议错误
        var sender = new MailKitEmailSender(_options, _httpClientFactoryMock.Object, _loggerMock.Object);

        // Act
        var result = await sender.SendAsync(new EmailMessage { Subject = "Subject", Body = "Body" });

        // Assert
        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("no recipient");
    }

    [Fact]
    public async Task SendToAsync_Should_Return_Failure_When_Address_Is_Blank()
    {
        // Arrange - 单收件人重定向到多收件人路径后，空地址仍应返回失败结果而不是抛异常
        var sender = new MailKitEmailSender(_options, _httpClientFactoryMock.Object, _loggerMock.Object);

        // Act
        var result = await sender.SendToAsync("   ", null, "Subject", "Body");

        // Assert
        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("no recipient");
    }

    [Fact]
    public async Task SendAsync_Should_Route_Every_Address_Through_The_Dev_Override()
    {
        // Arrange - 单测 EmailEnvelope.RedirectTo 只证明「重定向本身是密的」，
        // 这条证明发送器**确实调用了它**，且投递用的正是重定向之后的那份信封
        var logged = new List<string>();
        var loggerMock = new Mock<ILogger<MailKitEmailSender>>();
        loggerMock
            .Setup(l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(invocation => logged.Add(invocation.Arguments[2]?.ToString() ?? string.Empty)));

        var options = new NotificationOptions
        {
            MailSender = new MailSenderOptions
            {
                FromEmail = "test@example.com",
                FromName = "Test Sender",
                SmtpServer = string.Empty, // 立即失败，测试不碰网络
                SmtpPort = 587,
                DevOverrideEmail = "dev@localhost"
            }
        };
        var sender = new MailKitEmailSender(options, _httpClientFactoryMock.Object, loggerMock.Object);

        // Act
        var result = await sender.SendAsync(new EmailMessage
        {
            To = [new EmailAddress("claims@insurer.example", "Claims Intake")],
            Cc = [new EmailAddress("adjuster@insurer.example", "A. Adjuster")],
            Bcc = [new EmailAddress("file@ourfirm.example")],
            Subject = "Claim 12345",
            Body = "Body"
        });

        // Assert
        result.Success.ShouldBeFalse();

        var deliveryLog = logged.Single(m => m.StartsWith("Failed to send email to "));
        deliveryLog.ShouldContain("dev@localhost");
        deliveryLog.ShouldNotContain("insurer.example");
        deliveryLog.ShouldNotContain("ourfirm.example");
    }

    // 注意: 实际的 SMTP 发送测试需要真实的 SMTP 服务器或使用集成测试
    // 这里我们只测试配置验证和基本逻辑
}

/// <summary>
/// IEmailSender 的多收件人默认实现测试
/// </summary>
public class EmailSenderDefaultImplementationTests
{
    /// <summary>
    /// 只实现了原有单收件人方法的历史实现 —— 它必须仍然满足接口（源码兼容）
    /// </summary>
    private sealed class LegacyEmailSender : IEmailSender
    {
        public Task<SendResult> SendToAsync(string to, string? name, string subject, string body, bool isHtml = true, List<EmailAttachment>? attachments = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SendResult.CreateSuccess("legacy"));
        }
    }

    [Fact]
    public async Task Legacy_Implementation_Should_Still_Serve_The_Single_Address_Method()
    {
        // Act
        var result = await new LegacyEmailSender().SendToAsync("a@example.com", null, "Subject", "Body");

        // Assert
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Default_SendAsync_Should_Fail_Loudly_Instead_Of_Silently_Degrading()
    {
        // Arrange - 不支持多收件人的实现绝不能退化成「各发一封」或「只发第一个地址」：
        // 那会把一封抄送多方的函件换成另一种消息，而且毫无症状
        IEmailSender sender = new LegacyEmailSender();

        // Act
        var result = await sender.SendAsync(new EmailMessage
        {
            To = [new EmailAddress("a@example.com")],
            Cc = [new EmailAddress("b@example.com")],
            Subject = "Subject",
            Body = "Body"
        });

        // Assert
        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain(nameof(LegacyEmailSender));
        result.FailureReason!.ShouldContain("does not support multi-recipient");
    }
}

/// <summary>
/// HttpSmsSender 单元测试
/// </summary>
public class HttpSmsSenderTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<HttpSmsSender>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly NotificationOptions _options;

    public HttpSmsSenderTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<HttpSmsSender>>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        _options = new NotificationOptions
        {
            MaxConcurrency = 5,
            SmsSender = new SmsSenderOptions
            {
                Provider = "Twilio",
                TwilioAccountSid = "test_account_sid",
                TwilioAuthToken = "test_auth_token",
                TwilioFromPhoneNumber = "+1234567890"
            }
        };
    }

    [Fact]
    public void Constructor_Should_Initialize_Successfully()
    {
        // Act
        var sender = new HttpSmsSender(_options, _httpClientFactoryMock.Object, _loggerMock.Object);

        // Assert
        sender.ShouldNotBeNull();
    }

    [Fact]
    public async Task SendToAsync_Should_Return_Failure_When_Options_Not_Configured()
    {
        // Arrange
        var optionsWithoutSmsSender = new NotificationOptions();
        var sender = new HttpSmsSender(optionsWithoutSmsSender, _httpClientFactoryMock.Object, _loggerMock.Object);

        // Act
        var result = await sender.SendToAsync("+1234567890", "Test message");

        // Assert
        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("not configured");
    }

    [Fact]
    public async Task SendToAsync_Should_Return_Failure_When_Provider_Unsupported()
    {
        // Arrange
        var optionsWithUnsupportedProvider = new NotificationOptions
        {
            SmsSender = new SmsSenderOptions
            {
                Provider = "UnsupportedProvider",
                TwilioAccountSid = "test",
                TwilioAuthToken = "test"
            }
        };
        var sender = new HttpSmsSender(optionsWithUnsupportedProvider, _httpClientFactoryMock.Object, _loggerMock.Object);

        // Act
        var result = await sender.SendToAsync("+1234567890", "Test message");

        // Assert
        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("Unsupported");
    }

    [Fact]
    public async Task SendToAsync_Should_Send_Via_Twilio_Successfully()
    {
        // Arrange
        var httpClient = new HttpClient(_httpMessageHandlerMock.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var responseContent = "{\"sid\":\"SM123456\",\"status\":\"sent\"}";
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Created,
                Content = new StringContent(responseContent)
            });

        var sender = new HttpSmsSender(_options, _httpClientFactoryMock.Object, _loggerMock.Object);

        // Act
        var result = await sender.SendToAsync("+1234567890", "Test message");

        // Assert
        result.Success.ShouldBeTrue();
        result.ExternalMessageId.ShouldNotBeNullOrEmpty();
    }

}

/// <summary>
/// PushSender 单元测试
/// </summary>
public class PushSenderTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<PushSender>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly NotificationOptions _options;

    public PushSenderTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<PushSender>>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        _options = new NotificationOptions
        {
            MaxConcurrency = 5,
            PushSender = new PushSenderOptions
            {
                Provider = "Firebase",
                FirebaseProjectId = "test_project_id"
            }
        };
    }

    [Fact]
    public void Constructor_Should_Initialize_Successfully()
    {
        // Act
        var sender = new PushSender(_options, _loggerMock.Object);

        // Assert
        sender.ShouldNotBeNull();
    }

    [Fact]
    public async Task SendToAsync_Should_Return_Failure_When_Options_Not_Configured()
    {
        // Arrange
        var optionsWithoutPushSender = new NotificationOptions();
        var sender = new PushSender(optionsWithoutPushSender, _loggerMock.Object);

        // Act
        var result = await sender.SendToAsync("device_token", "Title", "Body");

        // Assert
        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("not configured");
    }

}
