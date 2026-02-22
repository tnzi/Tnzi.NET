using TemplateEntity = Tnzi.Template.Entities.Template;

namespace Tnzi.Template.Tests.Services;

/// <summary>
/// TemplateStoreService 文件系统模板加载测试
/// </summary>
public class TemplateStoreServiceFileSystemTests : IDisposable
{
    private readonly string _testTemplatesDir;
    private readonly Mock<IRepository<TemplateEntity, Guid>> _repositoryMock;
    private readonly TemplateFileParser _fileParser;
    private readonly TemplateStoreService _service;

    public TemplateStoreServiceFileSystemTests()
    {
        // 创建临时测试目录
        _testTemplatesDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testTemplatesDir);

        _repositoryMock = new Mock<IRepository<TemplateEntity, Guid>>();
        _fileParser = new TemplateFileParser();

        var templateOptions = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
        {
            TemplateRootPath = "Templates",
            EnableFileSystemTemplates = true,
            TemplateExtension = ".cshtml"
        });

        var environmentMock = new Mock<IHostEnvironment>();
        environmentMock.Setup(x => x.ContentRootPath).Returns(_testTemplatesDir);

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(x => x.GetService(typeof(IHostEnvironment))).Returns(environmentMock.Object);

        // 设置 ILoggerFactory（ApplicationService.Logger 需要）
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(loggerFactory.Object);

        _service = new TemplateStoreService(
            _repositoryMock.Object,
            serviceProvider.Object,
            _fileParser,
            templateOptions);
    }

    /// <summary>
    /// 设置仓储的 IQueryable 成员，使 LINQ 扩展方法能正常工作
    /// </summary>
    private void SetupQueryable(List<TemplateEntity> templates)
    {
        var mockQueryable = templates.BuildMock();
        _repositoryMock.As<IQueryable<TemplateEntity>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _repositoryMock.As<IQueryable<TemplateEntity>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _repositoryMock.As<IQueryable<TemplateEntity>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _repositoryMock.As<IQueryable<TemplateEntity>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    [Fact]
    public async Task GetTemplateAsync_WhenNotFoundInDatabase_ShouldLoadFromFileSystem()
    {
        // Arrange - 数据库中没有模板
        SetupQueryable(new List<TemplateEntity>());

        // 创建模板文件
        var templateDir = Path.Combine(_testTemplatesDir, "Templates", "Notification", "Email");
        Directory.CreateDirectory(templateDir);
        var templateFile = Path.Combine(templateDir, "WelcomeEmail.cshtml");

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
        var result = await _service.GetTemplateAsync("WelcomeEmail", "Notification", "Email");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("WelcomeEmail", result.Data.TemplateName);
        Assert.Equal("Notification", result.Data.Module);
        Assert.Equal("Email", result.Data.Category);
        Assert.Equal("Welcome to @Model.AppName!", result.Data.SubjectTemplate);
        Assert.Contains("Welcome to", result.Data.ContentTemplate);
        Assert.Contains("Thank you for registering", result.Data.ContentTemplate);
        Assert.Equal("DefaultEmail", result.Data.DefaultLayoutName);
    }

    [Fact]
    public async Task GetTemplateAsync_WhenFoundInDatabase_ShouldNotLoadFromFileSystem()
    {
        // Arrange - 数据库中有模板
        var dbTemplate = new TemplateEntity
        {
            Id = Guid.NewGuid(),
            TemplateName = "WelcomeEmail",
            Module = "Notification",
            Category = "Email",
            SubjectTemplate = "Database Subject",
            ContentTemplate = "Database Content",
            IsActive = true
        };

        SetupQueryable(new List<TemplateEntity> { dbTemplate });

        // Act
        var result = await _service.GetTemplateAsync("WelcomeEmail", "Notification", "Email");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Database Subject", result.Data.SubjectTemplate);
        Assert.Equal("Database Content", result.Data.ContentTemplate);
    }

    [Fact]
    public async Task GetTemplateAsync_WhenFileSystemDisabled_ShouldNotLoadFromFileSystem()
    {
        // Arrange - 使用禁用文件系统模板的服务
        var templateOptions = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
        {
            TemplateRootPath = "Templates",
            EnableFileSystemTemplates = false,
            TemplateExtension = ".cshtml"
        });

        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(loggerFactory.Object);

        var repositoryMock = new Mock<IRepository<TemplateEntity, Guid>>();
        var emptyQueryable = new List<TemplateEntity>().BuildMock();
        repositoryMock.As<IQueryable<TemplateEntity>>()
            .Setup(q => q.Provider).Returns(emptyQueryable.Provider);
        repositoryMock.As<IQueryable<TemplateEntity>>()
            .Setup(q => q.Expression).Returns(emptyQueryable.Expression);
        repositoryMock.As<IQueryable<TemplateEntity>>()
            .Setup(q => q.ElementType).Returns(emptyQueryable.ElementType);
        repositoryMock.As<IQueryable<TemplateEntity>>()
            .Setup(q => q.GetEnumerator()).Returns(() => emptyQueryable.GetEnumerator());

        var service = new TemplateStoreService(
            repositoryMock.Object,
            serviceProvider.Object,
            _fileParser,
            templateOptions);

        // Act
        var result = await service.GetTemplateAsync("WelcomeEmail", "Notification", "Email");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Null(result.Data);
    }

    public void Dispose()
    {
        // 清理临时目录
        if (Directory.Exists(_testTemplatesDir))
        {
            Directory.Delete(_testTemplatesDir, true);
        }
    }
}
