using Microsoft.Extensions.Logging.Abstractions;

namespace Tnzi.Template.Tests;

/// <summary>
/// <c>Template:AdditionalSearchPaths</c> 的每一项都是一个<b>完整的模板根</b>（与 <c>TemplateRootPath</c> 同级），
/// 文件查找、布局查找、布局扫描三条路都必须按这一种语义解析。
/// </summary>
/// <remarks>
/// <b>被保护的缺陷</b>：<c>TemplateOptionsPostConfigure</c> 与文档都把附加项当完整模板根
/// （启动时把每个模块程序集旁的 <c>&lt;dir&gt;/Templates</c> 加进去），而 <c>TemplateFileService</c> 与
/// <c>LayoutStoreService</c> 对每个附加项再拼一次 <c>TemplateRootPath</c>，得到 <c>&lt;dir&gt;/Templates/Templates</c> ——
/// 一个不存在的目录。于是程序集扫描每次启动都记一行「Added N template search paths」却一个模板都找不到，
/// 而照文档配 <c>["D:/shared/Templates"]</c> 的部署实际搜的是 <c>D:/shared/Templates/Templates</c>，
/// 得到的只是 404「Template not found」。布局扫描（管理端列表）则用的是第三种语义：
/// <c>&lt;searchRoot&gt;/Layouts</c>，连 <c>TemplateRootPath</c> 都没拼，从来列不出任何文件布局。
/// </remarks>
public class AdditionalSearchPathsTests : IDisposable
{
    private readonly string _primaryRoot;
    private readonly string _additionalRoot;
    private readonly ServiceProvider _serviceProvider;

    public AdditionalSearchPathsTests()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"Tnzi_AddlPaths_{Guid.NewGuid():N}");
        _primaryRoot = Path.Combine(baseDir, "Primary", "Templates");
        _additionalRoot = Path.Combine(baseDir, "Plugin", "Templates");
        Directory.CreateDirectory(_primaryRoot);
        Directory.CreateDirectory(_additionalRoot);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        // 布局列表的数据库分支走 ProjectTo，需要已初始化的 Mapster
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        var baseDir = Path.GetDirectoryName(Path.GetDirectoryName(_primaryRoot))!;
        if (Directory.Exists(baseDir))
        {
            try { Directory.Delete(baseDir, recursive: true); } catch { /* 临时目录清理失败不影响断言 */ }
        }
        GC.SuppressFinalize(this);
    }

    private IOptions<TemplateOptions> Options() => Microsoft.Extensions.Options.Options.Create(new TemplateOptions
    {
        TemplateRootPath = _primaryRoot,
        AdditionalSearchPaths = [_additionalRoot],
        EnableFileSystemTemplates = true,
        TemplateExtension = ".cshtml"
    });

    private static async Task<string> WriteAsync(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private static Mock<IRepository<Layout, Guid>> EmptyLayoutRepository()
    {
        var repository = new Mock<IRepository<Layout, Guid>>();
        var empty = new List<Layout>().BuildMock();
        repository.As<IQueryable<Layout>>().Setup(q => q.Provider).Returns(empty.Provider);
        repository.As<IQueryable<Layout>>().Setup(q => q.Expression).Returns(empty.Expression);
        repository.As<IQueryable<Layout>>().Setup(q => q.ElementType).Returns(empty.ElementType);
        repository.As<IQueryable<Layout>>().Setup(q => q.GetEnumerator()).Returns(() => empty.GetEnumerator());
        return repository;
    }

    private LayoutStoreService CreateLayoutStore()
        => new(EmptyLayoutRepository().Object, _serviceProvider, new TemplateFileParser(), Options());

    [Fact]
    public async Task Additional_search_paths_are_used_as_template_roots_verbatim()
    {
        await WriteAsync(_additionalRoot, Path.Combine("Notification", "Email", "Foo.cshtml"), "Foo @Model.X");
        var fileService = new TemplateFileService(new TemplateFileParser(), Options(), _serviceProvider, NullLogger<TemplateFileService>.Instance);

        var info = await fileService.FindTemplateAsync("Foo", "Notification", "Email");

        Assert.NotNull(info);
        Assert.Equal("Foo @Model.X", info!.ContentTemplate);
        Assert.Equal("Notification", info.Module);
        Assert.Equal("Email", info.Category);
    }

    [Fact]
    public async Task The_primary_root_wins_over_an_additional_root_for_the_same_template()
    {
        await WriteAsync(_primaryRoot, Path.Combine("Notification", "Email", "Foo.cshtml"), "primary");
        await WriteAsync(_additionalRoot, Path.Combine("Notification", "Email", "Foo.cshtml"), "additional");
        var fileService = new TemplateFileService(new TemplateFileParser(), Options(), _serviceProvider, NullLogger<TemplateFileService>.Instance);

        var info = await fileService.FindTemplateAsync("Foo", "Notification", "Email");

        Assert.Equal("primary", info!.ContentTemplate);
    }

    /// <summary>
    /// 程序集扫描加进来的是 <c>&lt;dir&gt;/Templates</c>：它下面的文件按绝对路径读取时必须被认作在根内，
    /// 而 <c>&lt;dir&gt;/Templates/Templates</c> 这种拼过头的根是认不出这个文件的。
    /// </summary>
    [Fact]
    public async Task Module_template_directories_scanned_by_PostConfigure_are_resolvable()
    {
        var options = Options().Value;
        options.AdditionalSearchPaths.Clear();
        // TemplateOptionsPostConfigure 扫的是进程里已加载的程序集，注入不了目录；这里按它的写法逐字模拟：
        // 程序集所在目录 + "Templates"（PostConfigure 里的 Path.Combine(directoryName, "Templates")）
        var moduleDir = Path.GetDirectoryName(_additionalRoot)!;
        options.AdditionalSearchPaths.Add(Path.Combine(moduleDir, "Templates"));

        var absolute = await WriteAsync(_additionalRoot, Path.Combine("Finance", "Print", "Check.cshtml"), "check");
        var fileService = new TemplateFileService(new TemplateFileParser(), Microsoft.Extensions.Options.Options.Create(options), _serviceProvider, NullLogger<TemplateFileService>.Instance);

        var byAbsolutePath = await fileService.ReadTemplateAsync(absolute);
        var listed = await fileService.ListTemplatesAsync("Finance");

        Assert.NotNull(byAbsolutePath);
        Assert.Single(listed);
        Assert.Equal("Check", listed[0].Name);
    }

    [Fact]
    public async Task Layouts_resolve_from_additional_search_paths()
    {
        await WriteAsync(_additionalRoot, Path.Combine("Layouts", "Email", "_Plugin.cshtml"), "<html>@Html.Raw(Model.Content)</html>");

        var result = await CreateLayoutStore().GetLayoutAsync("Plugin", "Notification", "Email");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Plugin", result.Data!.LayoutName);
        Assert.Equal("Email", result.Data.Category);
    }

    [Fact]
    public async Task Default_layout_resolves_from_additional_search_paths()
    {
        await WriteAsync(_additionalRoot, Path.Combine("Layouts", "Email", "_Default.cshtml"), "---\nisDefault: true\n---\n<html>@Html.Raw(Model.Content)</html>");

        var result = await CreateLayoutStore().GetDefaultLayoutAsync("Notification", "Email");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Email", result.Data!.Category);
    }

    [Fact]
    public async Task File_system_layouts_are_listed_from_every_template_root()
    {
        await WriteAsync(_primaryRoot, Path.Combine("Layouts", "Email", "_DefaultEmail.cshtml"), "<html>@Html.Raw(Model.Content)</html>");
        await WriteAsync(_additionalRoot, Path.Combine("Layouts", "Print", "_Check.cshtml"), "<html>@Html.Raw(Model.Content)</html>");

        var result = await CreateLayoutStore().QueryLayoutsAsync(new QueryLayoutRequest { IncludeFileSource = true, PageSize = 50 });

        Assert.True(result.Succeeded, result.Message);
        var items = result.Data!.Items.ToList();
        Assert.Equal(2, items.Count);
        // 与加载路径同一口径：Layouts/{category}/_{name}，名称去掉下划线前缀，模块不在路径里
        Assert.Contains(items, i => i.LayoutName == "DefaultEmail" && i.Category == "Email" && i.Source == "FileSystem");
        Assert.Contains(items, i => i.LayoutName == "Check" && i.Category == "Print" && i.Source == "FileSystem");
    }

    [Fact]
    public async Task File_system_layout_listing_filters_by_category()
    {
        await WriteAsync(_primaryRoot, Path.Combine("Layouts", "Email", "_DefaultEmail.cshtml"), "x");
        await WriteAsync(_additionalRoot, Path.Combine("Layouts", "Print", "_Check.cshtml"), "x");

        var result = await CreateLayoutStore().QueryLayoutsAsync(new QueryLayoutRequest { IncludeFileSource = true, Category = "Print", PageSize = 50 });

        var items = result.Data!.Items.ToList();
        Assert.Single(items);
        Assert.Equal("Check", items[0].LayoutName);
    }
}
