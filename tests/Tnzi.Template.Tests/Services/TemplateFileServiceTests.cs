namespace Tnzi.Template.Tests.Services;

/// <summary>
/// ITemplateFileService —— 模板文件自述内容（front matter）的公开读取入口。
/// 消费方此前只能直接注入 Tnzi.Template.Internal.TemplateFileParser 才拿得到这些键。
/// </summary>
public class TemplateFileServiceTests : IDisposable
{
    private readonly string _contentRoot;

    public TemplateFileServiceTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), $"Tnzi_TemplateFileSvc_{Guid.NewGuid()}");
        Directory.CreateDirectory(_contentRoot);
    }

    private TemplateFileService CreateService(bool enabled = true)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
        {
            TemplateRootPath = "Templates",
            TemplateExtension = ".cshtml",
            EnableFileSystemTemplates = enabled
        });

        var environment = new Mock<IHostEnvironment>();
        environment.Setup(e => e.ContentRootPath).Returns(_contentRoot);

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(IHostEnvironment))).Returns(environment.Object);

        return new TemplateFileService(new TemplateFileParser(), options, serviceProvider.Object);
    }

    private string WriteTemplate(string module, string? category, string name, string content)
    {
        var dir = string.IsNullOrEmpty(category)
            ? Path.Combine(_contentRoot, "Templates", module)
            : Path.Combine(_contentRoot, "Templates", module, category);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + ".cshtml");
        File.WriteAllText(path, content);
        return path;
    }

    private const string TemplateWithCustomKeys = """
---
subject: Monthly Statement
layout: DefaultEmail
description: Sent on the first of every month
metadata:
  group: billing
  printOnly: true
  replaces:
    - statement-v1
    - statement-v2
---
<p>Hello @Model.Name</p>
""";

    [Fact]
    public async Task FindTemplateAsync_ExposesCustomFrontMatterKeys()
    {
        WriteTemplate("Billing", "Email", "Statement", TemplateWithCustomKeys);
        var service = CreateService();

        var info = await service.FindTemplateAsync("Statement", "Billing", "Email");

        Assert.NotNull(info);
        // 键名完全由模板作者定义，框架不做任何约定：原样交出
        Assert.Equal("billing", info.Metadata["group"]);
        // 标量一律是字符串：YAML 里的 true 不会变成 bool（无类型信息可推），
        // 消费方要自己转。这里把它钉住，免得实现换了以后消费方的判断静默变味
        Assert.Equal("true", info.Metadata["printOnly"]);
        Assert.Contains("statement-v1", ((IEnumerable<object>)info.Metadata["replaces"]).Select(v => v.ToString()));
    }

    [Fact]
    public async Task FindTemplateAsync_CarriesTopLevelDescription()
    {
        WriteTemplate("Billing", "Email", "Statement", TemplateWithCustomKeys);
        var service = CreateService();

        var info = await service.FindTemplateAsync("Statement", "Billing", "Email");

        // front matter 顶层的 description 此前解析出来就被丢弃
        Assert.NotNull(info);
        Assert.Equal("Sent on the first of every month", info.Description);
        Assert.Equal("Monthly Statement", info.SubjectTemplate);
        Assert.Equal("DefaultEmail", info.DefaultLayoutName);
    }

    [Fact]
    public async Task FindTemplateAsync_DerivesModuleAndCategoryFromPath()
    {
        WriteTemplate("Billing", "Email/Monthly", "Statement", TemplateWithCustomKeys);
        var service = CreateService();

        var info = await service.FindTemplateAsync("Statement", "Billing", "Email/Monthly");

        Assert.NotNull(info);
        Assert.Equal("Billing", info.Module);
        Assert.Equal("Email/Monthly", info.Category);
        Assert.Equal("Statement", info.Name);
    }

    [Fact]
    public async Task FindTemplateAsync_WithoutCategory_LeavesCategoryEmpty()
    {
        WriteTemplate("Billing", null, "Statement", TemplateWithCustomKeys);
        var service = CreateService();

        var info = await service.FindTemplateAsync("Statement", "Billing");

        Assert.NotNull(info);
        Assert.Equal("Billing", info.Module);
        Assert.Equal(string.Empty, info.Category);
    }

    [Fact]
    public async Task FindTemplateAsync_WhenFileMissing_ReturnsNull()
    {
        var service = CreateService();

        Assert.Null(await service.FindTemplateAsync("NoSuchTemplate", "Billing", "Email"));
    }

    [Fact]
    public async Task ReadTemplateAsync_AcceptsPathRelativeToTemplateRoot()
    {
        WriteTemplate("Billing", "Email", "Statement", TemplateWithCustomKeys);
        var service = CreateService();

        var info = await service.ReadTemplateAsync(Path.Combine("Billing", "Email", "Statement.cshtml"));

        Assert.NotNull(info);
        Assert.Equal("billing", info.Metadata["group"]);
    }

    [Fact]
    public async Task ReadTemplateAsync_AcceptsAbsolutePathInsideTemplateRoot()
    {
        var path = WriteTemplate("Billing", "Email", "Statement", TemplateWithCustomKeys);
        var service = CreateService();

        var info = await service.ReadTemplateAsync(path);

        Assert.NotNull(info);
        Assert.Equal(path, info.FilePath);
    }

    [Fact]
    public async Task ReadTemplateAsync_RefusesPathOutsideTemplateRoot()
    {
        // 模板根之外的 .cshtml 不是模板：模板名/模块名可能来自消费方数据，
        // 放行越界路径等于让任意文件被当模板读走（与渲染引擎的路径校验同口径）
        var outside = Path.Combine(_contentRoot, "Secrets");
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "NotATemplate.cshtml");
        await File.WriteAllTextAsync(outsideFile, "@Model.Secret");

        var service = CreateService();

        Assert.Null(await service.ReadTemplateAsync(outsideFile));
        Assert.Null(await service.ReadTemplateAsync(Path.Combine("..", "Secrets", "NotATemplate.cshtml")));
    }

    [Fact]
    public async Task ListTemplatesAsync_ReturnsEveryFileWithItsDeclaredMetadata()
    {
        WriteTemplate("Billing", "Email", "Statement", TemplateWithCustomKeys);
        WriteTemplate("Billing", "Email", "Reminder", "<p>no front matter</p>");
        WriteTemplate("Crm", "Email", "Welcome", TemplateWithCustomKeys);

        var service = CreateService();
        var all = await service.ListTemplatesAsync();

        Assert.Equal(3, all.Count);
        var statement = all.Single(t => t.Name == "Statement");
        Assert.Equal("Billing", statement.Module);
        Assert.Equal("Email", statement.Category);
        Assert.Equal("billing", statement.Metadata["group"]);

        // 没有 front matter 的文件照样列出，正文即全文
        var reminder = all.Single(t => t.Name == "Reminder");
        Assert.Empty(reminder.Metadata);
        Assert.Contains("no front matter", reminder.ContentTemplate);
    }

    [Fact]
    public async Task ListTemplatesAsync_FiltersByModuleAndCategory()
    {
        WriteTemplate("Billing", "Email", "Statement", TemplateWithCustomKeys);
        WriteTemplate("Billing", "Sms", "Statement", TemplateWithCustomKeys);
        WriteTemplate("Crm", "Email", "Welcome", TemplateWithCustomKeys);

        var service = CreateService();

        Assert.Equal(2, (await service.ListTemplatesAsync("Billing")).Count);
        Assert.Single(await service.ListTemplatesAsync("Billing", "Sms"));
        Assert.Equal(2, (await service.ListTemplatesAsync(category: "Email")).Count);
    }

    [Fact]
    public async Task WhenFileSystemTemplatesDisabled_EverythingIsEmpty()
    {
        WriteTemplate("Billing", "Email", "Statement", TemplateWithCustomKeys);
        var service = CreateService(enabled: false);

        // 播种一类的调用方要能区分"配置关着"和"一个模板都没有"
        Assert.False(service.IsEnabled);
        Assert.Null(await service.FindTemplateAsync("Statement", "Billing", "Email"));
        Assert.Empty(await service.ListTemplatesAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
        {
            try
            {
                Directory.Delete(_contentRoot, true);
            }
            catch (IOException)
            {
                // 清理失败不影响断言结果
            }
        }
    }
}
