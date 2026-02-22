using TemplateEntity = Tnzi.Template.Entities.Template;

namespace Tnzi.Template.Tests.Integration;

/// <summary>
/// TemplateStoreService 文件系统模板加载集成测试
/// </summary>
public class TemplateStoreServiceFileSystemIntegrationTests : IDisposable
{
    private readonly string _testTemplatesDir;
    private readonly IServiceProvider _serviceProvider;
    private readonly TemplateStoreService _service;
    private readonly TestTemplateDbContext _dbContext;

    public TemplateStoreServiceFileSystemIntegrationTests()
    {
        // 创建临时测试目录
        _testTemplatesDir = Path.Combine(Path.GetTempPath(), $"Tnzi_TemplateTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testTemplatesDir);

        // 配置服务
        var services = new ServiceCollection();

        // 添加内存数据库
        services.AddDbContext<TestTemplateDbContext>(options =>
            options.UseInMemoryDatabase($"TemplateTestDb_{Guid.NewGuid()}"));

        // Mock ICurrentUser
        var currentUserMock = new Mock<ICurrentUser>();
        services.AddSingleton(currentUserMock.Object);

        // 添加 Repository
        services.AddScoped<IRepository<TemplateEntity, Guid>>(sp =>
        {
            var dbContext = sp.GetRequiredService<TestTemplateDbContext>();
            return new EFCoreRepository<TestTemplateDbContext, TemplateEntity, Guid>(dbContext, null, sp, null);
        });

        // 添加文件系统相关服务
        services.AddSingleton<TemplateFileParser>();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(_testTemplatesDir));
        var templateOptions = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
        {
            TemplateRootPath = "Templates",
            EnableFileSystemTemplates = true,
            TemplateExtension = ".cshtml"
        });
        services.AddSingleton<IOptions<TemplateOptions>>(templateOptions);
        services.AddSingleton<ILogger<TemplateStoreService>>(new TestLogger<TemplateStoreService>());

        // 添加 TemplateStoreService（构造函数注入 TemplateFileParser 和 IOptions<TemplateOptions>）
        services.AddScoped<TemplateStoreService>();

        _serviceProvider = services.BuildServiceProvider();
        _service = _serviceProvider.GetRequiredService<TemplateStoreService>();
        _dbContext = _serviceProvider.GetRequiredService<TestTemplateDbContext>();
    }

    [Fact]
    public async Task GetTemplateAsync_WhenNotFoundInDatabase_ShouldLoadFromFileSystem()
    {
        // Arrange - 确保数据库中没有模板
        _dbContext.Templates.RemoveRange(_dbContext.Templates);
        await _dbContext.SaveChangesAsync();

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
    <p>Thank you for registering with us. Your account has been successfully created.</p>
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
        // Arrange - 在数据库中创建模板
        var dbTemplate = new TemplateEntity
        {
            Id = Guid.NewGuid(),
            TemplateName = "WelcomeEmail",
            Module = "Notification",
            Category = "Email",
            SubjectTemplate = "Database Subject",
            ContentTemplate = "Database Content",
            IsActive = true,
            IsDeleted = false
        };

        _dbContext.Templates.Add(dbTemplate);
        await _dbContext.SaveChangesAsync();

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
        // Arrange - 禁用文件系统模板
        var services = new ServiceCollection();
        services.AddDbContext<TestTemplateDbContext>(options =>
            options.UseInMemoryDatabase($"TemplateTestDb_{Guid.NewGuid()}"));

        services.AddScoped<IRepository<TemplateEntity, Guid>>(sp =>
        {
            var dbContext = sp.GetRequiredService<TestTemplateDbContext>();
            return new EFCoreRepository<TestTemplateDbContext, TemplateEntity, Guid>(dbContext, null, sp, null);
        });

        var currentUserMock2 = new Mock<ICurrentUser>();
        services.AddSingleton(currentUserMock2.Object);

        services.AddSingleton<TemplateFileParser>();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(_testTemplatesDir));
        var templateOptions2 = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
        {
            TemplateRootPath = "Templates",
            EnableFileSystemTemplates = false, // 禁用文件系统模板
            TemplateExtension = ".cshtml"
        });
        services.AddSingleton<IOptions<TemplateOptions>>(templateOptions2);
        services.AddSingleton<ILogger<TemplateStoreService>>(new TestLogger<TemplateStoreService>());
        services.AddScoped<TemplateStoreService>();

        var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<TemplateStoreService>();
        var dbContext = serviceProvider.GetRequiredService<TestTemplateDbContext>();

        // 确保数据库中没有模板
        dbContext.Templates.RemoveRange(dbContext.Templates);
        await dbContext.SaveChangesAsync();

        // Act
        var result = await service.GetTemplateAsync("WelcomeEmail", "Notification", "Email");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task GetTemplateAsync_WithTemplateInFileSystem_ShouldExtractSubjectAndContent()
    {
        // Arrange
        var templateDir = Path.Combine(_testTemplatesDir, "Templates", "Notification", "Email");
        Directory.CreateDirectory(templateDir);
        var templateFile = Path.Combine(templateDir, "TestEmail.cshtml");

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
        var result = await _service.GetTemplateAsync("TestEmail", "Notification", "Email");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Test Subject @Model.Name", result.Data.SubjectTemplate);
        Assert.Contains("Hello @Model.Name!", result.Data.ContentTemplate);
        Assert.Contains("This is test content", result.Data.ContentTemplate);
        Assert.Equal("TestLayout", result.Data.DefaultLayoutName);
    }

    public void Dispose()
    {
        // 清理临时目录
        if (Directory.Exists(_testTemplatesDir))
        {
            try
            {
                Directory.Delete(_testTemplatesDir, true);
            }
            catch
            {
                // 忽略清理错误
            }
        }

        _dbContext?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }
}

/// <summary>
/// 测试用的 DbContext
/// </summary>
public class TestTemplateDbContext : TnziDbContext<TestTemplateDbContext>
{
    public TestTemplateDbContext(
        DbContextOptions<TestTemplateDbContext> options,
        ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<TemplateEntity> Templates { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<TemplateEntity>(entity =>
        {
            entity.ToTable("Template_Template");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TemplateName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Module).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Category).HasMaxLength(50);
        });
    }
}

/// <summary>
/// 测试用的 HostEnvironment
/// </summary>
public class TestHostEnvironment : IHostEnvironment
{
    public TestHostEnvironment(string contentRootPath)
    {
        ContentRootPath = contentRootPath;
        ContentRootFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(contentRootPath);
    }

    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "TestApp";
    public string ContentRootPath { get; set; }
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
}

/// <summary>
/// 测试用的 Logger
/// </summary>
public class TestLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}
