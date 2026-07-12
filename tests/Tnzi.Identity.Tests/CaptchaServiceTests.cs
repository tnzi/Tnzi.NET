using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

public class CaptchaServiceTests
{
    private readonly Mock<IOptionsSnapshot<IdentityOptions>> _identityOptionsMock;
    private readonly Mock<ICache> _cacheMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    private readonly CaptchaService _captchaService;

    public CaptchaServiceTests()
    {
        _identityOptionsMock = new Mock<IOptionsSnapshot<IdentityOptions>>();
        _identityOptionsMock.Setup(x => x.Value).Returns(new IdentityOptions
        {
            Captcha = new CaptchaOptions()
        });

        _cacheMock = new Mock<ICache>();
        _cacheMock.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(Task.CompletedTask);

        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _captchaService = new CaptchaService(_identityOptionsMock.Object, _serviceProviderMock.Object, _cacheMock.Object);
    }

    [Fact]
    public async Task GenerateAsync_WithValidPurpose_ReturnsCaptchaResult()
    {
        // Arrange
        var purpose = "login";

        _cacheMock.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _captchaService.GenerateAsync(purpose);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.CaptchaId);
        Assert.NotNull(result.ImageBytes);
        Assert.True(result.ImageBytes.Length > 0);
        Assert.True(result.ExpirationSeconds > 0);
    }

    [Fact]
    public async Task GenerateAsync_WithoutCache_ThrowsException()
    {
        // Arrange
        var service = new CaptchaService(_identityOptionsMock.Object, _serviceProviderMock.Object, null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateAsync("login"));
    }

    [Fact]
    public async Task VerifyAsync_WithValidCode_ReturnsTrue()
    {
        // Arrange
        var captchaId = Guid.NewGuid().ToString("N");
        var captchaCode = "ABCD";
        var purpose = "login";
        var cacheKey = Tnzi.Caching.CacheKeys.Identity.Captcha(purpose, captchaId);

        _cacheMock.Setup(x => x.GetAsync<string>(cacheKey))
            .ReturnsAsync(captchaCode.ToLower());

        _cacheMock.Setup(x => x.RemoveAsync(cacheKey))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _captchaService.VerifyAsync(captchaId, captchaCode, purpose);

        // Assert
        Assert.True(result);
        _cacheMock.Verify(x => x.RemoveAsync(cacheKey), Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_WithInvalidCode_ReturnsFalse()
    {
        // Arrange
        var captchaId = Guid.NewGuid().ToString("N");
        var captchaCode = "ABCD";
        var wrongCode = "XYZ";
        var purpose = "login";
        var cacheKey = Tnzi.Caching.CacheKeys.Identity.Captcha(purpose, captchaId);

        _cacheMock.Setup(x => x.GetAsync<string>(cacheKey))
            .ReturnsAsync(captchaCode.ToLower());

        // Act
        var result = await _captchaService.VerifyAsync(captchaId, wrongCode, purpose);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task VerifyAsync_WithExpiredCode_ReturnsFalse()
    {
        // Arrange
        var captchaId = Guid.NewGuid().ToString("N");
        var captchaCode = "ABCD";
        var purpose = "login";
        var cacheKey = Tnzi.Caching.CacheKeys.Identity.Captcha(purpose, captchaId);

        _cacheMock.Setup(x => x.GetAsync<string>(cacheKey))
            .ReturnsAsync((string?)null); // 已过期，缓存中不存在

        // Act
        var result = await _captchaService.VerifyAsync(captchaId, captchaCode, purpose);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RecordLoginFailureAsync_WithValidIdentifier_RecordsFailure()
    {
        // Arrange
        var identifier = "testuser";
        var cacheKey = Tnzi.Caching.CacheKeys.Identity.LoginFailure(identifier);

        _cacheMock.Setup(x => x.GetAsync<int?>(cacheKey))
            .ReturnsAsync(2);

        _cacheMock.Setup(x => x.SetAsync(cacheKey, 3, It.IsAny<TimeSpan>()))
            .Returns(Task.CompletedTask);

        // Act
        await _captchaService.RecordLoginFailureAsync(identifier);

        // Assert
        _cacheMock.Verify(x => x.SetAsync(cacheKey, 3, It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task GetLoginFailureCountAsync_WithValidIdentifier_ReturnsCount()
    {
        // Arrange
        var identifier = "testuser";
        var cacheKey = Tnzi.Caching.CacheKeys.Identity.LoginFailure(identifier);

        _cacheMock.Setup(x => x.GetAsync<int?>(cacheKey))
            .ReturnsAsync(5);

        // Act
        var count = await _captchaService.GetLoginFailureCountAsync(identifier);

        // Assert
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task ClearLoginFailureAsync_WithValidIdentifier_ClearsFailure()
    {
        // Arrange
        var identifier = "testuser";
        var cacheKey = Tnzi.Caching.CacheKeys.Identity.LoginFailure(identifier);

        _cacheMock.Setup(x => x.RemoveAsync(cacheKey))
            .Returns(Task.CompletedTask);

        // Act
        await _captchaService.ClearLoginFailureAsync(identifier);

        // Assert
        _cacheMock.Verify(x => x.RemoveAsync(cacheKey), Times.Once);
    }

    [Fact]
    public async Task IsCaptchaRequiredAsync_WithHighFailureCount_ReturnsTrue()
    {
        // Arrange
        var identifier = "testuser";
        var cacheKey = Tnzi.Caching.CacheKeys.Identity.LoginFailure(identifier);

        _identityOptionsMock.Setup(x => x.Value).Returns(new IdentityOptions
        {
            Captcha = new CaptchaOptions { CaptchaFailThreshold = 3 }
        });

        var service = new CaptchaService(_identityOptionsMock.Object, _serviceProviderMock.Object, _cacheMock.Object);

        _cacheMock.Setup(x => x.GetAsync<int?>(cacheKey))
            .ReturnsAsync(5); // 超过阈值

        // Act
        var result = await service.IsCaptchaRequiredAsync(identifier);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsCaptchaRequiredAsync_WithLowFailureCount_ReturnsFalse()
    {
        // Arrange
        var identifier = "testuser";
        var cacheKey = Tnzi.Caching.CacheKeys.Identity.LoginFailure(identifier);

        _identityOptionsMock.Setup(x => x.Value).Returns(new IdentityOptions
        {
            Captcha = new CaptchaOptions { CaptchaFailThreshold = 3 }
        });

        var service = new CaptchaService(_identityOptionsMock.Object, _serviceProviderMock.Object, _cacheMock.Object);

        _cacheMock.Setup(x => x.GetAsync<int?>(cacheKey))
            .ReturnsAsync(1); // 低于阈值

        // Act
        var result = await service.IsCaptchaRequiredAsync(identifier);

        // Assert
        Assert.False(result);
    }
}