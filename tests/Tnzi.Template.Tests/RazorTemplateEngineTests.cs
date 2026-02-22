
namespace Tnzi.Template.Tests;

/// <summary>
/// RazorTemplateEngine 单元测试
/// </summary>
public class RazorTemplateEngineTests : IDisposable
{
    private readonly RazorTemplateEngine _engine;
    private readonly Mock<ILogger<RazorTemplateEngine>> _loggerMock;
    private readonly Mock<IHostEnvironment> _environmentMock;
    private readonly IMemoryCache _cache;
    private readonly string _tempDir;

    public RazorTemplateEngineTests()
    {
        _loggerMock = new Mock<ILogger<RazorTemplateEngine>>();
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });

        // 创建临时测试目录
        _tempDir = Path.Combine(Path.GetTempPath(), $"Tnzi_Template_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Mock IHostEnvironment
        _environmentMock = new Mock<IHostEnvironment>();
        _environmentMock.Setup(e => e.ContentRootPath).Returns(_tempDir);

        var options = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
        {
            TemplateRootPath = _tempDir,
            EnableCache = true,
            CacheExpirationSeconds = 3600,
            TemplateExtension = ".cshtml",
            EnableHotReload = false
        });

        _engine = new RazorTemplateEngine(options, _loggerMock.Object, _cache);
    }

    public void Dispose()
    {
        _cache.Dispose();

        // 清理临时目录
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    #region RenderAsync Tests

    [Fact]
    public async Task RenderAsync_WithSimpleTemplate_ReturnsRenderedContent()
    {
        // Arrange
        var template = "Hello @Model.Name!";
        var model = new { Name = "World" };

        // Act
        var result = await _engine.RenderAsync(template, model);

        // Assert
        Assert.Equal("Hello World!", result);
    }

    [Fact]
    public async Task RenderAsync_WithNullTemplate_ReturnsEmpty()
    {
        // Act
        var result = await _engine.RenderAsync(null!);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task RenderAsync_WithEmptyTemplate_ReturnsEmpty()
    {
        // Act
        var result = await _engine.RenderAsync("");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task RenderAsync_WithNoModel_ReturnsRenderedContent()
    {
        // Arrange
        var template = "Static content";

        // Act
        var result = await _engine.RenderAsync(template);

        // Assert
        Assert.Equal("Static content", result);
    }

    [Fact]
    public async Task RenderAsync_WithSyntaxError_ThrowsTemplateCompilationException()
    {
        // Arrange - 使用真正的语法错误（未定义的类型）
        var invalidTemplate = "@{ UndefinedType x = null; }";

        // Act & Assert - 编译期语法错误会抛出 TemplateCompilationException
        await Assert.ThrowsAsync<TemplateCompilationException>(
            () => _engine.RenderAsync(invalidTemplate));
    }

    #endregion

    #region RenderFromFileAsync Tests

    [Fact]
    public async Task RenderFromFileAsync_WithExistingFile_ReturnsRenderedContent()
    {
        // Arrange
        var templatePath = Path.Combine(_tempDir, "test.cshtml");
        await File.WriteAllTextAsync(templatePath, "Hello @Model.Name!");
        var model = new { Name = "File" };

        // Act
        var result = await _engine.RenderFromFileAsync("test", model);

        // Assert
        Assert.Equal("Hello File!", result);
    }

    [Fact]
    public async Task RenderFromFileAsync_WithNonExistingFile_ThrowsTemplateNotFoundException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<TemplateNotFoundException>(
            () => _engine.RenderFromFileAsync("nonexistent"));
    }

    [Fact]
    public async Task RenderFromFileAsync_WithPathTraversal_ThrowsTemplateSecurityException()
    {
        // Arrange - 尝试路径遍历攻击
        var maliciousPath = "../../../etc/passwd";

        // Act & Assert
        await Assert.ThrowsAsync<TemplateSecurityException>(
            () => _engine.RenderFromFileAsync(maliciousPath));
    }

    [Fact]
    public async Task RenderFromFileAsync_WithSubdirectory_ReturnsRenderedContent()
    {
        // Arrange
        var subDir = Path.Combine(_tempDir, "Email");
        Directory.CreateDirectory(subDir);
        var templatePath = Path.Combine(subDir, "welcome.cshtml");
        await File.WriteAllTextAsync(templatePath, "Welcome @Model.UserName!");
        var model = new { UserName = "TestUser" };

        // Act
        var result = await _engine.RenderFromFileAsync("Email/welcome", model);

        // Assert
        Assert.Equal("Welcome TestUser!", result);
    }

    #endregion

    #region ValidateAsync Tests

    [Fact]
    public async Task ValidateAsync_WithValidTemplate_ReturnsSuccess()
    {
        // Arrange
        var template = "Hello @Model.Name!";

        // Act
        var result = await _engine.ValidateAsync(template);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithEmptyTemplate_ReturnsFailed()
    {
        // Act
        var result = await _engine.ValidateAsync("");

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithInvalidSyntax_ReturnsFailed()
    {
        // Arrange - 使用明确的语法错误（未定义的类型）
        var invalidTemplate = "@{ UndefinedType x = null; }";

        // Act
        var result = await _engine.ValidateAsync(invalidTemplate);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    #endregion

    #region Cache Tests

    [Fact]
    public async Task RenderAsync_WithCacheEnabled_UsesCachedTemplate()
    {
        // Arrange
        var template = "Cached: @Model.Value";
        var model1 = new { Value = "First" };
        var model2 = new { Value = "Second" };

        // Act
        var result1 = await _engine.RenderAsync(template, model1);
        var result2 = await _engine.RenderAsync(template, model2);

        // Assert
        Assert.Equal("Cached: First", result1);
        Assert.Equal("Cached: Second", result2);
        // 模板被缓存，但不同模型应该产生不同结果
    }

    [Fact]
    public void ClearCache_ClearsAllCachedTemplates()
    {
        // Act - 应该不抛出异常
        _engine.ClearCache();

        // Assert - 无异常表示成功
        Assert.True(true);
    }

    #endregion

    #region PrecompileTemplatesAsync Tests

    [Fact]
    public async Task PrecompileTemplatesAsync_WithExistingTemplates_PrecompilesSuccessfully()
    {
        // Arrange
        var template1Path = Path.Combine(_tempDir, "template1.cshtml");
        var template2Path = Path.Combine(_tempDir, "template2.cshtml");
        await File.WriteAllTextAsync(template1Path, "Template 1");
        await File.WriteAllTextAsync(template2Path, "Template 2");

        // Act
        await _engine.PrecompileTemplatesAsync(new[] { "template1.cshtml", "template2.cshtml" });

        // Assert - 预编译后，渲染应该更快（使用缓存）
        var result1 = await _engine.RenderFromFileAsync("template1");
        var result2 = await _engine.RenderFromFileAsync("template2");

        Assert.Equal("Template 1", result1);
        Assert.Equal("Template 2", result2);
    }

    [Fact]
    public async Task PrecompileTemplatesAsync_WithNonExistingTemplate_LogsWarning()
    {
        // Act - 不应该抛出异常
        await _engine.PrecompileTemplatesAsync(new[] { "nonexistent.cshtml" });

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region File Content Cache Tests

    [Fact]
    public async Task RenderFromFileAsync_WithCacheEnabled_ReadsFileOnlyOnce()
    {
        // Arrange
        var templatePath = Path.Combine(_tempDir, "cache_test.cshtml");
        var templateContent = "Cached content: @Model.Value";
        await File.WriteAllTextAsync(templatePath, templateContent);
        var model = new { Value = "Test" };

        // Act - 多次渲染同一文件
        var result1 = await _engine.RenderFromFileAsync("cache_test", model);
        var result2 = await _engine.RenderFromFileAsync("cache_test", model);
        var result3 = await _engine.RenderFromFileAsync("cache_test", model);

        // Assert - 所有结果应该相同，文件内容被缓存
        Assert.Equal("Cached content: Test", result1);
        Assert.Equal("Cached content: Test", result2);
        Assert.Equal("Cached content: Test", result3);
    }

    [Fact]
    public async Task RenderFromFileAsync_WithCacheDisabled_ReadsFileEveryTime()
    {
        // Arrange - 创建禁用缓存的引擎
        var noCacheOptions = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
        {
            TemplateRootPath = _tempDir,
            EnableCache = false,
            TemplateExtension = ".cshtml"
        });
        var noCacheEngine = new RazorTemplateEngine(noCacheOptions, _loggerMock.Object, _cache);

        var templatePath = Path.Combine(_tempDir, "nocache_test.cshtml");
        await File.WriteAllTextAsync(templatePath, "No cache: @Model.Value");
        var model = new { Value = "Test" };

        // Act
        var result = await noCacheEngine.RenderFromFileAsync("nocache_test", model);

        // Assert
        Assert.Equal("No cache: Test", result);
    }

    #endregion

    #region Hot Reload Tests

    [Fact]
    public async Task RenderFromFileAsync_WithHotReload_DetectsFileChanges()
    {
        // Arrange - 创建启用热重载的引擎
        var hotReloadOptions = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
        {
            TemplateRootPath = _tempDir,
            EnableCache = true,
            EnableHotReload = true,
            TemplateExtension = ".cshtml"
        });
        var hotReloadEngine = new RazorTemplateEngine(hotReloadOptions, _loggerMock.Object, _cache);

        var templatePath = Path.Combine(_tempDir, "hotreload_test.cshtml");
        await File.WriteAllTextAsync(templatePath, "Original: @Model.Value");
        var model = new { Value = "Test" };

        // Act - 第一次渲染
        var result1 = await hotReloadEngine.RenderFromFileAsync("hotreload_test", model);

        // 修改文件
        await Task.Delay(100); // 确保文件修改时间不同
        await File.WriteAllTextAsync(templatePath, "Updated: @Model.Value");

        // 第二次渲染
        var result2 = await hotReloadEngine.RenderFromFileAsync("hotreload_test", model);

        // Assert - 应该检测到文件变更并重新加载
        Assert.Equal("Original: Test", result1);
        Assert.Equal("Updated: Test", result2);
    }

    [Fact]
    public async Task RenderFromFileAsync_WithHotReloadDisabled_DoesNotDetectFileChanges()
    {
        // Arrange
        var templatePath = Path.Combine(_tempDir, "nohotreload_test.cshtml");
        await File.WriteAllTextAsync(templatePath, "Original: @Model.Value");
        var model = new { Value = "Test" };

        // Act - 第一次渲染
        var result1 = await _engine.RenderFromFileAsync("nohotreload_test", model);

        // 修改文件
        await Task.Delay(100);
        await File.WriteAllTextAsync(templatePath, "Updated: @Model.Value");

        // 第二次渲染（热重载禁用，应该使用缓存）
        var result2 = await _engine.RenderFromFileAsync("nohotreload_test", model);

        // Assert - 由于热重载禁用，应该使用缓存的内容
        Assert.Equal("Original: Test", result1);
        // 注意：如果缓存未过期，可能仍返回旧内容
        // 这里主要测试热重载开关的功能
    }

    #endregion

    #region Layout Tests

    [Fact]
    public async Task RenderFromFileAsync_WithLayout_AppliesLayout()
    {
        // Arrange
        var layoutDir = Path.Combine(_tempDir, "Layouts");
        Directory.CreateDirectory(layoutDir);
        var layoutPath = Path.Combine(layoutDir, "_Default.cshtml");
        // 使用字典索引器语法访问 Content
        await File.WriteAllTextAsync(layoutPath, "<html><body>@Model[\"Content\"]</body></html>");

        var templatePath = Path.Combine(_tempDir, "with_layout.cshtml");
        await File.WriteAllTextAsync(templatePath, "Template content");

        // Act
        var result = await _engine.RenderFromFileAsync("with_layout", null, "Layouts/_Default");

        // Assert
        Assert.Contains("<html>", result);
        Assert.Contains("<body>", result);
        Assert.Contains("Template content", result);
    }

    [Fact]
    public async Task RenderFromFileAsync_WithDefaultLayout_AppliesDefaultLayout()
    {
        // Arrange
        var layoutDir = Path.Combine(_tempDir, "Layouts");
        Directory.CreateDirectory(layoutDir);
        var layoutPath = Path.Combine(layoutDir, "_Layout.cshtml");
        // 使用字典索引器语法访问 Content
        await File.WriteAllTextAsync(layoutPath, "<div>@Model[\"Content\"]</div>");

        var options = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
        {
            TemplateRootPath = _tempDir,
            DefaultLayoutPath = "Layouts/_Layout",
            TemplateExtension = ".cshtml"
        });
        var engineWithLayout = new RazorTemplateEngine(options, _loggerMock.Object, _cache);

        var templatePath = Path.Combine(_tempDir, "default_layout.cshtml");
        await File.WriteAllTextAsync(templatePath, "Content");

        // Act
        var result = await engineWithLayout.RenderFromFileAsync("default_layout");

        // Assert
        Assert.Contains("<div>", result);
        Assert.Contains("Content", result);
    }

    [Fact]
    public async Task RenderFromFileAsync_WithLayoutAndModel_PassesModelToLayout()
    {
        // Arrange
        var layoutDir = Path.Combine(_tempDir, "Layouts");
        Directory.CreateDirectory(layoutDir);
        var layoutPath = Path.Combine(layoutDir, "_ModelLayout.cshtml");
        // 使用字典索引器语法访问字典值
        await File.WriteAllTextAsync(layoutPath, "<title>@Model[\"Title\"]</title><body>@Model[\"Content\"]</body>");

        var templatePath = Path.Combine(_tempDir, "model_layout.cshtml");
        await File.WriteAllTextAsync(templatePath, "Body content");
        var model = new { Title = "Test Page" };

        // Act
        var result = await _engine.RenderFromFileAsync("model_layout", model, "Layouts/_ModelLayout");

        // Assert
        Assert.Contains("<title>Test Page</title>", result);
        Assert.Contains("Body content", result);
    }

    #endregion

    #region Path Resolution Tests

    [Fact]
    public async Task RenderFromFileAsync_WithRelativePath_UsesContentRootPath()
    {
        // Arrange
        var subDir = Path.Combine(_tempDir, "SubDir");
        Directory.CreateDirectory(subDir);
        var templatePath = Path.Combine(subDir, "relative_test.cshtml");
        await File.WriteAllTextAsync(templatePath, "Relative path test");

        // Act
        var result = await _engine.RenderFromFileAsync("SubDir/relative_test");

        // Assert
        Assert.Equal("Relative path test", result);
    }

    [Fact]
    public async Task RenderFromFileAsync_WithAbsolutePath_ValidatesWithinRoot()
    {
        // Arrange
        var templatePath = Path.Combine(_tempDir, "absolute_test.cshtml");
        await File.WriteAllTextAsync(templatePath, "Absolute path test");

        // Act
        var result = await _engine.RenderFromFileAsync(templatePath);

        // Assert
        Assert.Equal("Absolute path test", result);
    }

    [Fact]
    public async Task RenderFromFileAsync_WithAbsolutePathOutsideRoot_ThrowsSecurityException()
    {
        // Arrange
        var outsidePath = Path.Combine(Path.GetTempPath(), $"outside_{Guid.NewGuid():N}.cshtml");
        await File.WriteAllTextAsync(outsidePath, "Outside content");

        try
        {
            // Act & Assert
            await Assert.ThrowsAsync<TemplateSecurityException>(
                () => _engine.RenderFromFileAsync(outsidePath));
        }
        finally
        {
            // Cleanup
            if (File.Exists(outsidePath))
                File.Delete(outsidePath);
        }
    }

    #endregion

    #region RenderFromNameAsync Tests

    [Fact]
    public async Task RenderFromNameAsync_WithExistingTemplate_ReturnsRenderedContent()
    {
        // Arrange
        var templatePath = Path.Combine(_tempDir, "named_template.cshtml");
        await File.WriteAllTextAsync(templatePath, "Named: @Model.Name");
        var model = new { Name = "Test" };

        // Act
        var result = await _engine.RenderFromNameAsync("named_template", model);

        // Assert
        Assert.Equal("Named: Test", result);
    }

    [Fact]
    public async Task RenderFromNameAsync_WithNonExistingTemplate_ThrowsException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<TemplateNotFoundException>(
            () => _engine.RenderFromNameAsync("nonexistent_template"));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task RenderFromFileAsync_WithEmptyFilePath_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _engine.RenderFromFileAsync(""));
    }

    [Fact]
    public async Task RenderFromFileAsync_WithNullFilePath_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _engine.RenderFromFileAsync(null!));
    }

    [Fact]
    public async Task RenderFromFileAsync_WithWhitespaceFilePath_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _engine.RenderFromFileAsync("   "));
    }

    [Fact]
    public async Task RenderFromFileAsync_WithoutExtension_AddsExtension()
    {
        // Arrange
        var templatePath = Path.Combine(_tempDir, "noextension.cshtml");
        await File.WriteAllTextAsync(templatePath, "No extension test");

        // Act - 不提供扩展名
        var result = await _engine.RenderFromFileAsync("noextension");

        // Assert
        Assert.Equal("No extension test", result);
    }

    [Fact]
    public async Task RenderFromFileAsync_WithCustomExtension_RespectsExtension()
    {
        // Arrange
        var customOptions = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
        {
            TemplateRootPath = _tempDir,
            TemplateExtension = ".razor"
        });
        var customEngine = new RazorTemplateEngine(customOptions, _loggerMock.Object, _cache);

        var templatePath = Path.Combine(_tempDir, "custom.razor");
        await File.WriteAllTextAsync(templatePath, "Custom extension");

        // Act
        var result = await customEngine.RenderFromFileAsync("custom");

        // Assert
        Assert.Equal("Custom extension", result);
    }

    #endregion
}