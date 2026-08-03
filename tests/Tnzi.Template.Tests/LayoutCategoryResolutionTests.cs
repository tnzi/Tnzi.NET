using TemplateEntity = Tnzi.Template.Entities.Template;

namespace Tnzi.Template.Tests;

/// <summary>
/// 布局按分类解析的测试。
///
/// 回归背景：<c>TemplateRenderService.ApplyLayoutByNameAsync</c> 早先固定用
/// <c>category: null</c> 去取布局，而文件系统布局是按分类分目录存放的
/// （<c>Templates/Layouts/{category}/_{name}.cshtml</c>）。框架内置的邮件布局在
/// <c>Layouts/Email/_DefaultEmail.cshtml</c>，拼出来的却是 <c>Layouts/_DefaultEmail.cshtml</c>，
/// 于是**所有**走内置模板的邮件都只发出内容片段，页眉页脚整个外壳丢失，
/// 日志里只留一行 "Layout 'DefaultEmail' not found ... skipping layout application"。
/// </summary>
public class LayoutCategoryResolutionTests : IDisposable
{
    // @ 前面必须留空白：Razor 把 "文字@表达式" 当成邮箱地址原样输出，不做代码转换
    private const string LayoutMarkup = "<html><body>LAYOUT-START @Html.Raw(Model.Content) LAYOUT-END</body></html>";

    private readonly RazorTemplateEngine _engine;
    private readonly IMemoryCache _cache;
    private readonly ServiceProvider _serviceProvider;
    private readonly string _tempDir;

    public LayoutCategoryResolutionTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        _tempDir = Path.Combine(Path.GetTempPath(), $"Tnzi_LayoutCategory_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var options = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
        {
            TemplateRootPath = _tempDir,
            EnableCache = false,
            TemplateExtension = ".cshtml"
        });

        _engine = new RazorTemplateEngine(options, new Mock<ILogger<RazorTemplateEngine>>().Object, _cache);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _cache.Dispose();
        _serviceProvider.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private static TemplateEntity EmailTemplate() => new()
    {
        TemplateName = "WelcomeEmail",
        Module = "Notification",
        Category = "Email",
        SubjectTemplate = "Welcome",
        ContentTemplate = "<p>BODY</p>",
        DefaultLayoutName = "DefaultEmail",
        IsActive = true
    };

    private TemplateRenderService CreateService(ILayoutStoreService layoutStore)
        => new(_serviceProvider, _engine, new Mock<ITemplateStoreService>().Object, layoutStore);

    [Fact]
    public async Task LayoutLookup_UsesTemplateCategory()
    {
        var layoutStore = new Mock<ILayoutStoreService>();
        layoutStore
            .Setup(s => s.GetLayoutAsync("DefaultEmail", "Notification", "Email", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new Layout { LayoutName = "DefaultEmail", LayoutContent = LayoutMarkup }));

        var result = await CreateService(layoutStore.Object).RenderAsync(EmailTemplate());

        Assert.True(result.Succeeded);
        Assert.Contains("LAYOUT-START", result.Data!.Content);
        Assert.Contains("BODY", result.Data.Content);
        layoutStore.Verify(
            s => s.GetLayoutAsync("DefaultEmail", "Notification", "Email", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// 数据库里的布局行分类可能为空，或布局直接放在 Layouts/ 根下：
    /// 带分类查不到时必须回退到不带分类，两种组织方式都要能用。
    /// </summary>
    [Fact]
    public async Task LayoutLookup_FallsBackToNoCategory()
    {
        var layoutStore = new Mock<ILayoutStoreService>();
        layoutStore
            .Setup(s => s.GetLayoutAsync("DefaultEmail", "Notification", "Email", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Layout>("Layout not found", 404));
        layoutStore
            .Setup(s => s.GetLayoutAsync("DefaultEmail", "Notification", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new Layout { LayoutName = "DefaultEmail", LayoutContent = LayoutMarkup }));

        var result = await CreateService(layoutStore.Object).RenderAsync(EmailTemplate());

        Assert.True(result.Succeeded);
        Assert.Contains("LAYOUT-START", result.Data!.Content);
    }

    /// <summary>
    /// 两种查找都落空时保持既有行为：不套布局、只发内容，且不让整封邮件失败。
    /// </summary>
    [Fact]
    public async Task MissingLayout_StillRendersBareContent()
    {
        var layoutStore = new Mock<ILayoutStoreService>();
        layoutStore
            .Setup(s => s.GetLayoutAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Layout>("Layout not found", 404));

        var result = await CreateService(layoutStore.Object).RenderAsync(EmailTemplate());

        Assert.True(result.Succeeded);
        Assert.DoesNotContain("LAYOUT-START", result.Data!.Content);
        Assert.Contains("BODY", result.Data.Content);
    }

    /// <summary>
    /// 端到端复现内置布局的真实布置：文件在 Layouts/Email/_DefaultEmail.cshtml，
    /// 只有带 category="Email" 去查才命中；不带分类查（回归前的固定行为）必然落空。
    /// </summary>
    [Fact]
    public async Task FileSystemLayout_IsFoundOnlyWithCategory()
    {
        var layoutDir = Path.Combine(_tempDir, "Layouts", "Email");
        Directory.CreateDirectory(layoutDir);
        await File.WriteAllTextAsync(Path.Combine(layoutDir, "_DefaultEmail.cshtml"), LayoutMarkup);

        var store = CreateFileSystemLayoutStore();

        var withCategory = await store.GetLayoutAsync("DefaultEmail", "Notification", "Email");
        var withoutCategory = await store.GetLayoutAsync("DefaultEmail", "Notification", null);

        Assert.True(withCategory.Succeeded);
        Assert.Contains("LAYOUT-START", withCategory.Data!.LayoutContent);
        Assert.False(withoutCategory.Succeeded);
    }

    /// <summary>
    /// 构造一个只走文件系统后备的 LayoutStoreService（数据库查询恒为空）
    /// </summary>
    private LayoutStoreService CreateFileSystemLayoutStore()
    {
        // IRepository<T, TKey> 继承 IQueryable<T>，Where 是 Queryable 扩展方法（不可 Setup），
        // 所以直接把 mock 的 IQueryable 成员接到一个空的可异步查询序列上
        var queryable = new List<Layout>().BuildMock();
        var repository = new Mock<IRepository<Layout, Guid>>();
        repository.Setup(r => r.Provider).Returns(queryable.Provider);
        repository.Setup(r => r.Expression).Returns(queryable.Expression);
        repository.Setup(r => r.ElementType).Returns(queryable.ElementType);
        repository.Setup(r => r.GetEnumerator()).Returns(() => queryable.GetEnumerator());

        var environment = new Mock<IHostEnvironment>();
        environment.Setup(e => e.ContentRootPath).Returns(_tempDir);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(environment.Object);
        var provider = services.BuildServiceProvider();

        var options = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
        {
            TemplateRootPath = _tempDir,
            EnableFileSystemTemplates = true,
            TemplateExtension = ".cshtml"
        });

        return new LayoutStoreService(repository.Object, provider, new TemplateFileParser(), options);
    }
}
