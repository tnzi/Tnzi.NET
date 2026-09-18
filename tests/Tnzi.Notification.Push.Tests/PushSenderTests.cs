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
        var sender = new PushSender(_options.PushSender!, _loggerMock.Object);

        // Assert
        sender.ShouldNotBeNull();
    }

    /// <summary>
    /// 「没配推送」不再是发送器自己的分支：构造函数只收一节推送配置，没有配置就构造不出来，
    /// 分档（Null / Unconfigured / 真实现）由模块注册决定。见 <c>PushSenderWiringTests</c>。
    /// </summary>
    [Fact]
    public void Constructor_Rejects_A_Missing_Profile()
    {
        Should.Throw<ArgumentNullException>(() => new PushSender(null!, _loggerMock.Object));
    }

}
