namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// NativeDocumentConverter 和 CliDocumentConverter 单元测试
/// </summary>
public class NativeDocumentConverterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly NativeDocumentConverter _nativeConverter;
    private readonly CliDocumentConverter _cliConverter;

    public NativeDocumentConverterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tnzi_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _nativeConverter = new NativeDocumentConverter(Mock.Of<ILogger<NativeDocumentConverter>>());
        _cliConverter = new CliDocumentConverter(Mock.Of<ILogger<CliDocumentConverter>>());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // 测试清理失败不影响结果
        }
    }

    #region NativeDocumentConverter.CanConvert

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".doc")]
    [InlineData(".docx")]
    [InlineData(".xls")]
    [InlineData(".xlsx")]
    [InlineData(".ppt")]
    [InlineData(".pptx")]
    [InlineData(".PDF")]
    [InlineData(".DOCX")]
    public void NativeConverter_CanConvert_SupportedExtensions_ReturnsTrue(string extension)
    {
        _nativeConverter.CanConvert(extension).ShouldBeTrue();
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".csv")]
    [InlineData(".json")]
    [InlineData(".html")]
    [InlineData(".md")]
    [InlineData("")]
    public void NativeConverter_CanConvert_UnsupportedExtensions_ReturnsFalse(string extension)
    {
        _nativeConverter.CanConvert(extension).ShouldBeFalse();
    }

    #endregion

    #region NativeDocumentConverter.ConvertToMarkdownAsync

    [Fact]
    public async Task NativeConverter_ConvertToMarkdownAsync_NonExistentFile_ReturnsNull()
    {
        var filePath = Path.Combine(_tempDir, "non_existent.pdf");

        var result = await _nativeConverter.ConvertToMarkdownAsync(filePath);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task NativeConverter_ConvertToMarkdownAsync_UnsupportedExtension_ReturnsNull()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(filePath, "test content");

        var result = await _nativeConverter.ConvertToMarkdownAsync(filePath);

        result.ShouldBeNull();
    }

    [Theory]
    [InlineData(".doc")]
    [InlineData(".xls")]
    [InlineData(".ppt")]
    public async Task NativeConverter_ConvertToMarkdownAsync_LegacyFormat_ReturnsPlaceholder(string extension)
    {
        var filePath = Path.Combine(_tempDir, $"test{extension}");
        await File.WriteAllTextAsync(filePath, "dummy");

        var result = await _nativeConverter.ConvertToMarkdownAsync(filePath);

        result.ShouldNotBeNull();
        result.ShouldContain("Legacy format");
        result.ShouldContain(extension);
    }

    #endregion

    #region CliDocumentConverter.CanConvert

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".doc")]
    [InlineData(".docx")]
    [InlineData(".xls")]
    [InlineData(".xlsx")]
    [InlineData(".ppt")]
    [InlineData(".pptx")]
    public void CliConverter_CanConvert_SupportedExtensions_ReturnsTrue(string extension)
    {
        _cliConverter.CanConvert(extension).ShouldBeTrue();
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".csv")]
    [InlineData(".json")]
    [InlineData("")]
    public void CliConverter_CanConvert_UnsupportedExtensions_ReturnsFalse(string extension)
    {
        _cliConverter.CanConvert(extension).ShouldBeFalse();
    }

    #endregion

    #region CliDocumentConverter.ConvertToMarkdownAsync

    [Fact]
    public async Task CliConverter_ConvertToMarkdownAsync_NonExistentFile_ReturnsNull()
    {
        var filePath = Path.Combine(_tempDir, "non_existent.pdf");

        var result = await _cliConverter.ConvertToMarkdownAsync(filePath);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task CliConverter_ConvertToMarkdownAsync_UnsupportedExtension_ReturnsNull()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(filePath, "test content");

        var result = await _cliConverter.ConvertToMarkdownAsync(filePath);

        result.ShouldBeNull();
    }

    #endregion
}
