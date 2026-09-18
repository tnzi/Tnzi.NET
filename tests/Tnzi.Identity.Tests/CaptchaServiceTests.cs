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

        _cacheMock.Setup(x => x.IncrementAsync(cacheKey, 1, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        // Act
        await _captchaService.RecordLoginFailureAsync(identifier);

        // Assert: 原子递增，不是读-改-写。
        _cacheMock.Verify(x => x.IncrementAsync(cacheKey, 1, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(x => x.SetAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Never);
    }

    /// <summary>
    /// ★ 此前是 <c>GetAsync&lt;int?&gt;</c> 再 <c>SetAsync(n + 1)</c>：并发的失败登录同时读到 k、各写 k+1，
    /// 最终计数远小于实际次数，「N 次失败后要验证码」的闸门可被并发喷洒推迟。
    /// 用真实的 <see cref="MemoryCacheService"/>（有锁的原子递增）而不是 mock：这条要证明的是计数不丢。
    /// </summary>
    [Fact]
    public async Task RecordLoginFailure_Concurrent_CountIsExact()
    {
        var cache = new MemoryCacheService(
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            new Mock<ILogger<MemoryCacheService>>().Object,
            Microsoft.Extensions.Options.Options.Create(new CachingOptions()));
        var service = new CaptchaService(_identityOptionsMock.Object, _serviceProviderMock.Object, cache);
        const int attempts = 50;

        await Task.WhenAll(Enumerable.Range(0, attempts).Select(_ => Task.Run(() => service.RecordLoginFailureAsync("sprayed"))));

        Assert.Equal(attempts, await service.GetLoginFailureCountAsync("sprayed"));
    }

    [Fact]
    public async Task GetLoginFailureCountAsync_WithValidIdentifier_ReturnsCount()
    {
        // Arrange
        var identifier = "testuser";
        var cacheKey = Tnzi.Caching.CacheKeys.Identity.LoginFailure(identifier);

        _cacheMock.Setup(x => x.GetCounterAsync(cacheKey, It.IsAny<CancellationToken>()))
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

        _cacheMock.Setup(x => x.GetCounterAsync(cacheKey, It.IsAny<CancellationToken>()))
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

        _cacheMock.Setup(x => x.GetCounterAsync(cacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1); // 低于阈值

        // Act
        var result = await service.IsCaptchaRequiredAsync(identifier);

        // Assert
        Assert.False(result);
    }
}