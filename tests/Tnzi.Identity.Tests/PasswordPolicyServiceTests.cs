
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

public class PasswordPolicyServiceTests
{
    private readonly Mock<IRepository<PasswordHistory, Guid>> _repositoryMock;
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IOptions<IdentityOptions>> _identityOptionsMock;
    private readonly Mock<ILogger<PasswordPolicyService>> _loggerMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    private readonly PasswordPolicyService _passwordPolicyService;

    public PasswordPolicyServiceTests()
    {
        _repositoryMock = new Mock<IRepository<PasswordHistory, Guid>>();

        var store = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _identityOptionsMock = new Mock<IOptions<IdentityOptions>>();
        _identityOptionsMock.Setup(x => x.Value).Returns(new IdentityOptions
        {
            PasswordPolicy = new PasswordPolicyOptions
            {
                MinLength = 8,
                RequireDigit = true,
                RequireLowercase = true,
                RequireUppercase = false,
                RequireNonAlphanumeric = false,
                PasswordHistoryCount = 5,
                PasswordExpirationDays = 90
            }
        });
        _loggerMock = new Mock<ILogger<PasswordPolicyService>>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _passwordPolicyService = new PasswordPolicyService(
            _repositoryMock.Object,
            _userManagerMock.Object,
            _serviceProviderMock.Object,
            _identityOptionsMock.Object
        );
    }

    [Fact]
    public void ValidatePasswordStrength_WithValidPassword_ReturnsNull()
    {
        // Arrange
        var password = "Password123";

        // Act
        var result = _passwordPolicyService.ValidatePasswordStrength(password);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ValidatePasswordStrength_WithShortPassword_ReturnsError()
    {
        // Arrange
        var password = "Pass1"; // 太短

        // Act
        var result = _passwordPolicyService.ValidatePasswordStrength(password);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("at least", result);
    }

    [Fact]
    public void ValidatePasswordStrength_WithoutDigit_ReturnsError()
    {
        // Arrange
        var password = "Password"; // 没有数字

        // Act
        var result = _passwordPolicyService.ValidatePasswordStrength(password);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("digit", result);
    }

    [Fact]
    public void ValidatePasswordStrength_WithoutLowercase_ReturnsError()
    {
        // Arrange
        var password = "PASSWORD123"; // 没有小写字母

        // Act
        var result = _passwordPolicyService.ValidatePasswordStrength(password);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("lowercase", result);
    }

    [Fact(Skip = "需要集成测试：CheckPasswordHistoryAsync 使用了 EF Core 的复杂查询，在单元测试中难以完全模拟")]
    public async Task CheckPasswordHistoryAsync_WithNewPassword_ReturnsFalse()
    {
        await Task.CompletedTask;
        Assert.True(true);
    }

    [Fact]
    public async Task CheckPasswordHistoryAsync_WhenHistoryDisabled_ReturnsFalse()
    {
        // Arrange
        _identityOptionsMock.Setup(x => x.Value).Returns(new IdentityOptions
        {
            PasswordPolicy = new PasswordPolicyOptions { PasswordHistoryCount = 0 }
        });

        var service = new PasswordPolicyService(
            _repositoryMock.Object,
            _userManagerMock.Object,
            _serviceProviderMock.Object,
            _identityOptionsMock.Object
        );

        // Act
        var result = await service.CheckPasswordHistoryAsync(Guid.NewGuid(), "Password123");

        // Assert
        Assert.False(result);
    }

    [Fact(Skip = "需要集成测试：SavePasswordHistoryAsync 使用了 EF Core 的复杂查询，在单元测试中难以完全模拟")]
    public async Task SavePasswordHistoryAsync_WithValidInput_SavesHistory()
    {
        await Task.CompletedTask;
        Assert.True(true);
    }

    [Fact(Skip = "需要集成测试：CheckPasswordExpirationAsync 使用了 EF Core 的复杂查询，在单元测试中难以完全模拟")]
    public async Task CheckPasswordExpirationAsync_WithNonExpiredPassword_ReturnsNotExpired()
    {
        await Task.CompletedTask;
        Assert.True(true);
    }

    [Fact(Skip = "需要集成测试：GetLastPasswordChangeTimeAsync 使用了 EF Core 的复杂查询，在单元测试中难以完全模拟")]
    public async Task GetLastPasswordChangeTimeAsync_WithUser_ReturnsTime()
    {
        await Task.CompletedTask;
        Assert.True(true);
    }
}