
namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// Null Sender 测试 (用于测试环境)
/// </summary>
public class NullSenderTests
{
    [Fact]
    public async Task NullEmailSender_Should_Always_Return_Success()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<NullEmailSender>>();
        var sender = new NullEmailSender(loggerMock.Object);

        // Act
        var result = await sender.SendToAsync("test@example.com", "Test", "Subject", "Body");

        // Assert
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task NullEmailSender_Should_Accept_Multi_Recipient_Messages()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<NullEmailSender>>();
        var sender = new NullEmailSender(loggerMock.Object);

        // Act
        var result = await sender.SendAsync(new EmailMessage
        {
            To = [new EmailAddress("claims@insurer.example", "Claims Intake")],
            Cc = [new EmailAddress("adjuster@insurer.example")],
            Subject = "Subject",
            Body = "Body"
        });

        // Assert
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task NullSmsSender_Should_Always_Return_Success()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<NullSmsSender>>();
        var sender = new NullSmsSender(loggerMock.Object);

        // Act
        var result = await sender.SendToAsync("+1234567890", "Test message");

        // Assert
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task NullPushSender_Should_Always_Return_Success()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<NullPushSender>>();
        var sender = new NullPushSender(loggerMock.Object);

        // Act
        var result = await sender.SendToAsync("device_token", "Title", "Body");

        // Assert
        result.Success.ShouldBeTrue();
    }
}