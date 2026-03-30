namespace Tnzi.AI.Tests.Tools;

/// <summary>
/// ViewImageTool 单元测试
/// </summary>
public class ViewImageToolTests : IDisposable
{
    private readonly string _tempDir;

    public ViewImageToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ViewImageToolTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    #region IsValidImageExtension

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".webp")]
    [InlineData(".gif")]
    [InlineData(".JPG")]
    [InlineData(".PNG")]
    public void IsValidImageExtension_ValidExtension_ReturnsTrue(string extension)
    {
        ViewImageTool.IsValidImageExtension(extension).ShouldBeTrue();
    }

    [Theory]
    [InlineData(".bmp")]
    [InlineData(".tiff")]
    [InlineData(".svg")]
    [InlineData(".pdf")]
    [InlineData(".txt")]
    [InlineData("")]
    public void IsValidImageExtension_InvalidExtension_ReturnsFalse(string extension)
    {
        ViewImageTool.IsValidImageExtension(extension).ShouldBeFalse();
    }

    #endregion

    #region GetMimeType

    [Theory]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".webp", "image/webp")]
    [InlineData(".gif", "image/gif")]
    public void GetMimeType_KnownExtension_ReturnsCorrectMimeType(string extension, string expectedMimeType)
    {
        ViewImageTool.GetMimeType(extension).ShouldBe(expectedMimeType);
    }

    [Theory]
    [InlineData(".bmp")]
    [InlineData(".svg")]
    [InlineData(".unknown")]
    public void GetMimeType_UnknownExtension_ReturnsDefaultMimeType(string extension)
    {
        ViewImageTool.GetMimeType(extension).ShouldBe("application/octet-stream");
    }

    #endregion

    #region ReadImageAsBase64Async

    [Fact]
    public async Task ReadImageAsBase64Async_ValidFile_ReturnsBase64String()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "test.png");
        var testBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // PNG header
        await File.WriteAllBytesAsync(filePath, testBytes);

        // Act
        var result = await ViewImageTool.ReadImageAsBase64Async(filePath);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(Convert.ToBase64String(testBytes));
    }

    [Fact]
    public async Task ReadImageAsBase64Async_NonExistentFile_ReturnsNull()
    {
        var filePath = Path.Combine(_tempDir, "nonexistent.png");

        var result = await ViewImageTool.ReadImageAsBase64Async(filePath);

        result.ShouldBeNull();
    }

    #endregion
}
