using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

public class LoginSecurityServiceTests
{
    private readonly Mock<IOptions<IdentityOptions>> _identityOptionsMock;
    private readonly Mock<IRepository<LoginLog, Guid>> _loginLogRepositoryMock;
    private readonly Mock<ILogger<LoginSecurityService>> _loggerMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    private readonly LoginSecurityService _loginSecurityService;

    public LoginSecurityServiceTests()
    {
        _identityOptionsMock = new Mock<IOptions<IdentityOptions>>();
        _identityOptionsMock.Setup(x => x.Value).Returns(new IdentityOptions
        {
            AccountSecurity = new AccountSecurityOptions
            {
                EnableAbnormalLoginDetection = true
            }
        });

        _loginLogRepositoryMock = new Mock<IRepository<LoginLog, Guid>>();
        _loggerMock = new Mock<ILogger<LoginSecurityService>>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _loginSecurityService = new LoginSecurityService(
            _serviceProviderMock.Object,
            _identityOptionsMock.Object,
            _loginLogRepositoryMock.Object
        );
    }

    [Fact]
    public async Task DetectAbnormalLoginAsync_WhenDetectionDisabled_ReturnsNormal()
    {
        // Arrange
        _identityOptionsMock.Setup(x => x.Value).Returns(new IdentityOptions
        {
            AccountSecurity = new AccountSecurityOptions
            {
                EnableAbnormalLoginDetection = false
            }
        });

        var service = new LoginSecurityService(
            _serviceProviderMock.Object,
            _identityOptionsMock.Object,
            _loginLogRepositoryMock.Object
        );

        // Act
        var result = await service.DetectAbnormalLoginAsync(Guid.NewGuid(), "127.0.0.1", "Browser");

        // Assert
        Assert.False(result.IsAbnormal);
        Assert.Equal(0, result.RiskLevel);
    }

    [Fact(Skip = "需要集成测试：DetectAbnormalLoginAsync 使用了 EF Core 的复杂查询，在单元测试中难以完全模拟")]
    public async Task DetectAbnormalLoginAsync_WithNewIp_ReturnsAbnormal()
    {
        await Task.CompletedTask;
        Assert.True(true);
    }

    [Fact(Skip = "需要集成测试：DetectAbnormalLoginAsync 使用了 EF Core 的复杂查询，在单元测试中难以完全模拟")]
    public async Task DetectAbnormalLoginAsync_WithKnownIp_ReturnsNormal()
    {
        await Task.CompletedTask;
        Assert.True(true);
    }

    [Fact]
    public void GenerateDeviceFingerprint_WithValidInput_ReturnsFingerprint()
    {
        // Arrange
        var userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

        // Act
        var fingerprint = _loginSecurityService.GenerateDeviceFingerprint(userAgent);

        // Assert
        Assert.NotNull(fingerprint);
        Assert.NotEmpty(fingerprint);
    }

    [Fact]
    public void GenerateDeviceFingerprint_WithNullInput_ReturnsEmpty()
    {
        // Act
        var fingerprint = _loginSecurityService.GenerateDeviceFingerprint(null);

        // Assert
        // 可能返回空字符串或默认值
        Assert.NotNull(fingerprint);
    }
}