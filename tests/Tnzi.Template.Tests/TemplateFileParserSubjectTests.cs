
namespace Tnzi.Template.Tests;

/// <summary>
/// 测试 TemplateFileParser 是否正确提取 subject
/// </summary>
public class TemplateFileParserSubjectTests : IDisposable
{
    private readonly string _testDir;
    private readonly TemplateFileParser _parser;

    public TemplateFileParserSubjectTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"TemplateTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _parser = new TemplateFileParser();
    }

    [Fact]
    public async Task ParseTemplateFileAsync_WithSubjectInFrontMatter_ShouldExtractSubject()
    {
        // Arrange
        var templateFile = Path.Combine(_testDir, "WelcomeEmail.cshtml");
        var templateContent = @"---
subject: Welcome to @Model.AppName!
layout: DefaultEmail
description: Welcome email template
---
<div style=""font-family: Arial, sans-serif;"">
    <h2>Welcome to @(Model.AppName ?? ""Our Platform"")!</h2>
    <p>Hi @(Model.UserName ?? ""there""),</p>
    <p>Thank you for registering with us.</p>
</div>";

        await File.WriteAllTextAsync(templateFile, templateContent);

        // Act
        var result = await _parser.ParseTemplateFileAsync(templateFile);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Welcome to @Model.AppName!", result.SubjectTemplate);
        Assert.Contains("Welcome to", result.ContentTemplate);
        Assert.Contains("Thank you for registering", result.ContentTemplate);
        Assert.Equal("DefaultEmail", result.DefaultLayoutName);
    }

    [Fact]
    public async Task ParseTemplateFileAsync_WithSubjectInFrontMatter_ShouldExtractContent()
    {
        // Arrange
        var templateFile = Path.Combine(_testDir, "TestEmail.cshtml");
        var templateContent = @"---
subject: Test Subject @Model.Name
layout: TestLayout
---
<div>
    <p>Hello @Model.Name!</p>
    <p>This is test content.</p>
</div>";

        await File.WriteAllTextAsync(templateFile, templateContent);

        // Act
        var result = await _parser.ParseTemplateFileAsync(templateFile);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test Subject @Model.Name", result.SubjectTemplate);
        Assert.Contains("Hello @Model.Name!", result.ContentTemplate);
        Assert.Contains("This is test content", result.ContentTemplate);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }
}