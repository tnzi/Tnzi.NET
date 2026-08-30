namespace Tnzi.Notification.Push.Tests;

/// <summary>
/// PushSender 单元测试
/// </summary>
public class PushSenderTests
{
    private readonly Mock<ILogger<PushSender>> _loggerMock;
    private readonly NotificationOptions _options;

    public PushSenderTests()
    {
        _loggerMock = new Mock<ILogger<PushSender>>();
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
