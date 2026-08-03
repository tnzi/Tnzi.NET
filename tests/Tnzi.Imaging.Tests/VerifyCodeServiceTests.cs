
namespace Tnzi.Imaging.Tests;

public class VerifyCodeServiceTests
{
    private readonly Mock<ICache> _cacheMock;
    private readonly ValidateCoder _coder;
    private readonly VerifyCodeService _service;
    private readonly VerifyCodeService _serviceWithoutCache;

    public VerifyCodeServiceTests()
    {
        _cacheMock = new Mock<ICache>();
        _coder = new ValidateCoder();
        _service = new VerifyCodeService(_coder, _cacheMock.Object);
        _serviceWithoutCache = new VerifyCodeService(_coder);
    }

    // ==================== GenerateAsync Tests ====================

    [Fact]
    public async Task GenerateAsync_ReturnsValidResult()
    {
        var result = await _service.GenerateAsync(4, VerifyCodeType.Number);

        result.ShouldNotBeNull();
        result.Code.Length.ShouldBe(4);
        result.ImageBytes.ShouldNotBeEmpty();
        result.Id.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GenerateAsync_NumberType_ContainsOnlyDigits()
    {
        var result = await _service.GenerateAsync(6, VerifyCodeType.Number);
        result.Code.ShouldAllBe(c => char.IsDigit(c));
    }

    [Fact]
    public async Task GenerateAsync_MixedType_ContainsAlphanumeric()
    {
        var result = await _service.GenerateAsync(8, VerifyCodeType.Mixed);
        result.Code.ShouldAllBe(c => char.IsLetterOrDigit(c));
    }

    [Fact]
    public async Task GenerateAsync_WithId_UsesProvidedId()
    {
        var result = await _service.GenerateAsync("my-id", 4, VerifyCodeType.Number);
        result.Id.ShouldBe("my-id");
    }

    [Fact]
    public async Task GenerateAsync_StoresInCache()
    {
        var result = await _service.GenerateAsync("test-id", 4, VerifyCodeType.Number, 10);

        _cacheMock.Verify(c => c.SetAsync(
            "VerifyCode:test-id",
            It.Is<string>(s => s.Length == 4),
            TimeSpan.FromMinutes(10),
            default), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_WithoutCache_StillWorks()
    {
        var result = await _serviceWithoutCache.GenerateAsync(4, VerifyCodeType.Number);

        result.ShouldNotBeNull();
        result.Code.ShouldNotBeNullOrEmpty();
        result.ImageBytes.ShouldNotBeEmpty();
    }

    // ==================== VerifyAsync Tests ====================

    [Fact]
    public async Task VerifyAsync_CorrectCode_ReturnsTrue()
    {
        _cacheMock.Setup(c => c.GetAsync<string>("VerifyCode:id1", default))
            .ReturnsAsync("1234");

        var result = await _service.VerifyAsync("id1", "1234");
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task VerifyAsync_CorrectCode_CaseInsensitive()
    {
        _cacheMock.Setup(c => c.GetAsync<string>("VerifyCode:id1", default))
            .ReturnsAsync("AbCd");

        var result = await _service.VerifyAsync("id1", "abcd", ignoreCase: true);
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task VerifyAsync_CorrectCode_CaseSensitive()
    {
        _cacheMock.Setup(c => c.GetAsync<string>("VerifyCode:id1", default))
            .ReturnsAsync("AbCd");

        var result = await _service.VerifyAsync("id1", "abcd", ignoreCase: false);
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task VerifyAsync_WrongCode_ReturnsFalse()
    {
        _cacheMock.Setup(c => c.GetAsync<string>("VerifyCode:id1", default))
            .ReturnsAsync("1234");

        var result = await _service.VerifyAsync("id1", "5678");
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task VerifyAsync_ExpiredCode_ReturnsFalse()
    {
        _cacheMock.Setup(c => c.GetAsync<string>("VerifyCode:id1", default))
            .ReturnsAsync((string?)null);

        var result = await _service.VerifyAsync("id1", "1234");
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task VerifyAsync_RemovesCacheAfterVerification()
    {
        _cacheMock.Setup(c => c.GetAsync<string>("VerifyCode:id1", default))
            .ReturnsAsync("1234");

        await _service.VerifyAsync("id1", "1234");

        _cacheMock.Verify(c => c.RemoveAsync("VerifyCode:id1", default), Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_EmptyId_ReturnsFalse()
    {
        var result = await _service.VerifyAsync("", "1234");
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task VerifyAsync_EmptyCode_ReturnsFalse()
    {
        var result = await _service.VerifyAsync("id1", "");
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task VerifyAsync_NoCacheRegistered_ReturnsFalse()
    {
        var result = await _serviceWithoutCache.VerifyAsync("id1", "1234");
        result.ShouldBeFalse();
    }

    // ==================== RemoveAsync Tests ====================

    [Fact]
    public async Task RemoveAsync_ValidId_RemovesFromCache()
    {
        await _service.RemoveAsync("id1");
        _cacheMock.Verify(c => c.RemoveAsync("VerifyCode:id1", default), Times.Once);
    }

    [Fact]
    public async Task RemoveAsync_EmptyId_DoesNothing()
    {
        await _service.RemoveAsync("");
        _cacheMock.Verify(c => c.RemoveAsync(It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task RemoveAsync_NoCacheRegistered_DoesNothing()
    {
        // Should not throw
        await _serviceWithoutCache.RemoveAsync("id1");
    }
}
