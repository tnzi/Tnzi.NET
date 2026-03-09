
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

    #region EvaluatePasswordStrength

    [Fact]
    public void EvaluatePasswordStrength_WithNull_ReturnsVeryWeakWithScoreZero()
    {
        // Act
        var result = _passwordPolicyService.EvaluatePasswordStrength(null!);

        // Assert
        Assert.Equal(0, result.Score);
        Assert.Equal(PasswordStrengthLevel.VeryWeak, result.Level);
        Assert.False(result.MeetsPolicy);
        Assert.Contains(result.Suggestions, s => s.Contains("empty"));
    }

    [Fact]
    public void EvaluatePasswordStrength_WithEmpty_ReturnsVeryWeakWithScoreZero()
    {
        // Act
        var result = _passwordPolicyService.EvaluatePasswordStrength(string.Empty);

        // Assert
        Assert.Equal(0, result.Score);
        Assert.Equal(PasswordStrengthLevel.VeryWeak, result.Level);
        Assert.False(result.MeetsPolicy);
        Assert.Single(result.Suggestions);
    }

    [Fact]
    public void EvaluatePasswordStrength_WithShortPassword_ReturnsLowScore()
    {
        // Arrange: "ab" is 2 chars, only lowercase
        // 长度评分: min(2*3, 30) = 6
        // 字符类型: hasLowercase = 10
        // 复杂度奖励: charTypeCount=1, no bonus
        // 总分 = 16

        // Act
        var result = _passwordPolicyService.EvaluatePasswordStrength("ab");

        // Assert
        Assert.True(result.Score < 40); // VeryWeak 或 Weak
        Assert.True(result.Score > 0);
        Assert.True(result.Level == PasswordStrengthLevel.VeryWeak || result.Level == PasswordStrengthLevel.Weak);
        Assert.False(result.MeetsPolicy); // 不满足 MinLength=8
    }

    [Fact]
    public void EvaluatePasswordStrength_WithAllCharTypes_ReturnsHighScore()
    {
        // Arrange: "MyP@ss1234!" has upper, lower, digit, special, length=11
        // 长度评分: min(11*3, 30) = 30
        // 字符类型: 10+10+10+10 = 40
        // 复杂度: charTypeCount=4, +10+5 = 15
        // 总分 = 85, clamped

        // Act
        var result = _passwordPolicyService.EvaluatePasswordStrength("MyP@ss1234!");

        // Assert
        Assert.True(result.Score >= 80);
        Assert.True(result.Level == PasswordStrengthLevel.Strong || result.Level == PasswordStrengthLevel.VeryStrong);
    }

    [Fact]
    public void EvaluatePasswordStrength_MeetsPolicy_WhenValidateReturnsNull()
    {
        // Arrange: policy requires MinLength=8, RequireDigit, RequireLowercase
        // "password1" meets all these requirements
        var password = "password1";

        // Act
        var result = _passwordPolicyService.EvaluatePasswordStrength(password);

        // Assert
        Assert.True(result.MeetsPolicy);
        // Verify ValidatePasswordStrength returns null for this password
        Assert.Null(_passwordPolicyService.ValidatePasswordStrength(password));
    }

    [Fact]
    public void EvaluatePasswordStrength_MeetsPolicy_False_WhenPasswordDoesNotMeetPolicy()
    {
        // Arrange: "short" is only 5 chars (MinLength=8), so fails policy
        var password = "short";

        // Act
        var result = _passwordPolicyService.EvaluatePasswordStrength(password);

        // Assert
        Assert.False(result.MeetsPolicy);
    }

    [Fact]
    public void EvaluatePasswordStrength_Suggestions_PopulatedForMissingCharTypes()
    {
        // Arrange: "aaaa1111" has lowercase + digit, no uppercase, no special
        var password = "aaaa1111";

        // Act
        var result = _passwordPolicyService.EvaluatePasswordStrength(password);

        // Assert
        Assert.Contains(result.Suggestions, s => s.Contains("uppercase"));
        Assert.Contains(result.Suggestions, s => s.Contains("special"));
    }

    [Fact]
    public void EvaluatePasswordStrength_Score_ClampedTo0And100()
    {
        // 非常长、各种类型都有的密码
        var password = "A1b@C2d#E3f$G4h%I5j^";

        // Act
        var result = _passwordPolicyService.EvaluatePasswordStrength(password);

        // Assert
        Assert.InRange(result.Score, 0, 100);
    }

    [Fact]
    public void EvaluatePasswordStrength_RepeatedCharacters_PenaltyApplied()
    {
        // "aaaaaa1" has very low distinct ratio (2/7 ≈ 0.286 < 0.5), should get -10 penalty
        var repeatedPassword = "aaaaaa1";
        // "abcdef1" has distinct ratio 7/7 = 1.0, no penalty
        var diversePassword = "abcdef1";

        // Act
        var repeatedResult = _passwordPolicyService.EvaluatePasswordStrength(repeatedPassword);
        var diverseResult = _passwordPolicyService.EvaluatePasswordStrength(diversePassword);

        // Assert: repeated password should have lower score than diverse password
        // Both have same length and similar char types, but repeated gets -10 penalty
        Assert.True(repeatedResult.Score < diverseResult.Score);
        Assert.Contains(repeatedResult.Suggestions, s => s.Contains("repeating"));
    }

    [Fact]
    public void EvaluatePasswordStrength_VeryLongAllTypes_ReturnsVeryStrong()
    {
        // 长度 >= 12 额外 +5, charTypeCount=4 +10+5
        var password = "MyStr0ng!Pass";

        // Act
        var result = _passwordPolicyService.EvaluatePasswordStrength(password);

        // Assert
        Assert.Equal(PasswordStrengthLevel.VeryStrong, result.Level);
        Assert.True(result.Score >= 80);
    }

    #endregion

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

}
