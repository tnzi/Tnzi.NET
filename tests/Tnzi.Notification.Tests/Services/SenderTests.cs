
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

    // 注意: 实际的 SMTP 发送测试需要真实的 SMTP 服务器或使用集成测试
    // 这里我们只测试配置验证和基本逻辑
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
