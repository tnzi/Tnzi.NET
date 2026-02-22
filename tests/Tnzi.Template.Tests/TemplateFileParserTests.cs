
namespace Tnzi.Template.Tests;

/// <summary>
/// TemplateFileParser 单元测试
/// </summary>
public class TemplateFileParserTests : IDisposable
{
    private readonly TemplateFileParser _parser;
    private readonly string _tempDir;

    public TemplateFileParserTests()
    {
        _parser = new TemplateFileParser();
        _tempDir = Path.Combine(Path.GetTempPath(), $"Tnzi_Parser_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    #region ParseTemplateFileAsync Tests

    [Fact]
    public async Task ParseTemplateFileAsync_WithFrontMatter_ParsesCorrectly()
    {
        // Arrange
        var content = @"---
subject: Welcome to @Model.SiteName!
layout: EmailDefault
description: Welcome email template
type: Email
---
<h1>Hello @Model.UserName!</h1>
<p>Welcome to our site.</p>";
        var filePath = Path.Combine(_tempDir, "welcome.cshtml");
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var result = await _parser.ParseTemplateFileAsync(filePath);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("welcome", result.Name);
        Assert.Equal("Welcome to @Model.SiteName!", result.SubjectTemplate);
        Assert.Equal("EmailDefault", result.DefaultLayoutName);
        Assert.Contains("<h1>Hello @Model.UserName!</h1>", result.ContentTemplate);
    }

    [Fact]
    public async Task ParseTemplateFileAsync_WithoutFrontMatter_ReturnsEmptyMetadata()
    {
        // Arrange
        var content = @"<h1>Simple Template</h1>
<p>No front matter here.</p>";
        var filePath = Path.Combine(_tempDir, "simple.cshtml");
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var result = await _parser.ParseTemplateFileAsync(filePath);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("simple", result.Name);
        Assert.Equal(string.Empty, result.SubjectTemplate);
        Assert.Null(result.DefaultLayoutName);
        Assert.Contains("<h1>Simple Template</h1>", result.ContentTemplate);
    }

    [Fact]
    public async Task ParseTemplateFileAsync_WithNonExistingFile_ReturnsNull()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "nonexistent.cshtml");

        // Act
        var result = await _parser.ParseTemplateFileAsync(filePath);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ParseTemplateFileAsync_WithEmptyFile_ReturnsEmptyContent()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "empty.cshtml");
        await File.WriteAllTextAsync(filePath, "");

        // Act
        var result = await _parser.ParseTemplateFileAsync(filePath);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("empty", result.Name);
        Assert.Equal(string.Empty, result.ContentTemplate);
    }

    [Fact]
    public async Task ParseTemplateFileAsync_SetsFilePathAndLastModified()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "metadata.cshtml");
        await File.WriteAllTextAsync(filePath, "Content");

        // Act
        var result = await _parser.ParseTemplateFileAsync(filePath);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(filePath, result.FilePath);
        Assert.NotNull(result.LastModified);
        Assert.True(result.LastModified > DateTime.MinValue);
    }

    #endregion

    #region ParseLayoutFileAsync Tests

    [Fact]
    public async Task ParseLayoutFileAsync_WithDefaultLayout_SetsIsDefaultTrue()
    {
        // Arrange
        var content = @"---
type: Email
isDefault: true
description: Default email layout
---
<!DOCTYPE html>
<html>
<body>@Model.Content</body>
</html>";
        var filePath = Path.Combine(_tempDir, "_EmailLayout.cshtml");
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var result = await _parser.ParseLayoutFileAsync(filePath);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("EmailLayout", result.Name);  // 移除下划线前缀
        Assert.True(result.IsDefault);
        Assert.Contains("@Model.Content", result.LayoutContent);
    }

    [Fact]
    public async Task ParseLayoutFileAsync_WithNonDefaultLayout_SetsIsDefaultFalse()
    {
        // Arrange
        var content = @"---
type: Email
description: Alternative email layout
---
<div>@Model.Content</div>";
        var filePath = Path.Combine(_tempDir, "_AltLayout.cshtml");
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var result = await _parser.ParseLayoutFileAsync(filePath);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("AltLayout", result.Name);
        Assert.False(result.IsDefault);
    }

    [Fact]
    public async Task ParseLayoutFileAsync_WithNonExistingFile_ReturnsNull()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "_nonexistent.cshtml");

        // Act
        var result = await _parser.ParseLayoutFileAsync(filePath);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region YAML Parsing Edge Cases

    [Fact]
    public async Task ParseTemplateFileAsync_WithMalformedYaml_ReturnsDefaultMetadata()
    {
        // Arrange - YAML 格式错误
        var content = @"---
subject: [invalid yaml
layout: broken
---
Content";
        var filePath = Path.Combine(_tempDir, "malformed.cshtml");
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var result = await _parser.ParseTemplateFileAsync(filePath);

        // Assert - 应该返回默认值而不是抛出异常
        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.SubjectTemplate);
    }

    [Fact]
    public async Task ParseTemplateFileAsync_WithCustomMetadata_ParsesMetadataDict()
    {
        // Arrange
        var content = @"---
subject: Test
metadata:
  priority: high
  category: notification
---
Content";
        var filePath = Path.Combine(_tempDir, "custom.cshtml");
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var result = await _parser.ParseTemplateFileAsync(filePath);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Metadata);
        // 注意：metadata 字段的解析取决于 YAML 解析器的行为
    }

    #endregion
}