using TemplateEntity = Tnzi.Template.Entities.Template;

namespace Tnzi.Template.Tests;

/// <summary>
/// 编码归属于<b>出口</b>而不是引擎：邮件主题、短信正文不是 HTML，对它们做 HTML 编码只会把
/// <c>O'Brien</c> 变成 <c>O&amp;#39;Brien</c>、把短信里的链接 <c>?token=x&amp;uid=y</c> 变成坏链接。
/// </summary>
/// <remarks>
/// <b>被保护的缺陷</b>：2026-09-04 起 <c>TemplateBase</c> 对每个 <c>@expr</c> 做 HTML 编码（那一半是对的，
/// 邮件正文与打印件必须编码），但主题与短信正文经<b>同一个</b>引擎、同一个基类渲染，没有任何一处
/// 说「这份输出不是 HTML」。无异常、无日志，发送状态显示成功，收件人看到的是实体。
/// </remarks>
public class TemplateOutputKindTests : IDisposable
{
    private readonly RazorTemplateEngine _engine;
    private readonly IMemoryCache _cache;
    private readonly ServiceProvider _serviceProvider;

    public TemplateOutputKindTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var options = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
        {
            TemplateRootPath = Path.GetTempPath(),
            EnableCache = true,
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
        GC.SuppressFinalize(this);
    }

    private TemplateRenderService CreateService()
        => new(_serviceProvider, _engine, new Mock<ITemplateStoreService>().Object);

    private static TemplateEntity Template(TemplateType type, string subject, string content) => new()
    {
        TemplateName = "T",
        Module = "Notification",
        Category = type.ToString(),
        Type = type,
        SubjectTemplate = subject,
        ContentTemplate = content,
        IsActive = true
    };

    #region 引擎：按输出类型选择是否编码

    [Fact]
    public async Task Engine_PlainText_DoesNotHtmlEncode()
    {
        var result = await _engine.RenderAsync("Hi @Model.Name", new { Name = "Dupont & Fils <O'Brien>" }, TemplateOutputKind.PlainText);

        Assert.Equal("Hi Dupont & Fils <O'Brien>", result);
    }

    [Fact]
    public async Task Engine_Html_StillEncodes()
    {
        var result = await _engine.RenderAsync("Hi @Model.Name", new { Name = "Dupont & Fils <O'Brien>" }, TemplateOutputKind.Html);

        Assert.Equal("Hi Dupont &amp; Fils &lt;O&#39;Brien&gt;", result);
    }

    [Fact]
    public async Task Engine_DefaultOverload_IsHtml()
    {
        var result = await _engine.RenderAsync("@Model.Name", new { Name = "a & b" });

        Assert.Equal("a &amp; b", result);
    }

    /// <summary>
    /// 编译缓存按内容哈希取键；两种输出类型编译出的是不同基类的两个类，必须各占一个条目，
    /// 否则先渲染的那一种决定后面所有渲染的编码行为。
    /// </summary>
    [Fact]
    public async Task PlainText_And_Html_Compilations_Do_Not_Share_A_Cache_Entry()
    {
        const string template = "Value: @Model.V";
        var model = new { V = "a & b" };

        var html = await _engine.RenderAsync(template, model, TemplateOutputKind.Html);
        var text = await _engine.RenderAsync(template, model, TemplateOutputKind.PlainText);
        var htmlAgain = await _engine.RenderAsync(template, model, TemplateOutputKind.Html);

        Assert.Equal("Value: a &amp; b", html);
        Assert.Equal("Value: a & b", text);
        Assert.Equal(html, htmlAgain);
        Assert.Equal(2, ((MemoryCache)_cache).Count);
    }

    [Fact]
    public async Task PlainText_Raw_IsANoOp()
    {
        var result = await _engine.RenderAsync("@Raw(Model.V) | @Html.Raw(Model.V) | @Model.V", new { V = "<b>&</b>" }, TemplateOutputKind.PlainText);

        Assert.Equal("<b>&</b> | <b>&</b> | <b>&</b>", result);
    }

    [Fact]
    public async Task PlainText_AttributeValues_AreNotEncoded()
    {
        // 短信里不会有属性，但纯文本基类的两条写入路径都不能编码，否则「文本里带引号的链接」照样坏
        var result = await _engine.RenderAsync("<a href=\"@Model.Url\">x</a>", new { Url = "https://x/?a=1&b=2" }, TemplateOutputKind.PlainText);

        Assert.Contains("?a=1&b=2", result);
        Assert.DoesNotContain("&amp;", result);
    }

    #endregion

    #region 渲染服务：主题恒纯文本，正文按模板类型

    [Fact]
    public async Task Subject_IsNotHtmlEncoded()
    {
        var template = Template(TemplateType.Email, "Invoice for @Model.CustomerName", "<p>@Model.CustomerName</p>");

        var result = await CreateService().RenderAsync(template, new { CustomerName = "Dupont & Fils, O'Brien" });

        Assert.True(result.Succeeded);
        Assert.Equal("Invoice for Dupont & Fils, O'Brien", result.Data!.Subject);
    }

    [Fact]
    public async Task EmailBody_StillEncodes()
    {
        var template = Template(TemplateType.Email, "S", "<p>@Model.CustomerName</p>");

        var result = await CreateService().RenderAsync(template, new { CustomerName = "<script>alert(1)</script> & co" });

        Assert.True(result.Succeeded);
        Assert.Equal("<p>&lt;script&gt;alert(1)&lt;/script&gt; &amp; co</p>", result.Data!.Content);
    }

    [Fact]
    public async Task SmsBody_IsNotHtmlEncoded()
    {
        var template = Template(TemplateType.Sms, "", "Reset here: @Model.Url");

        var result = await CreateService().RenderAsync(template, new { Url = "https://x/reset?token=x&uid=y" });

        Assert.True(result.Succeeded);
        Assert.Equal("Reset here: https://x/reset?token=x&uid=y", result.Data!.Content);
    }

    [Theory]
    [InlineData(TemplateType.Generic)]
    [InlineData(TemplateType.Page)]
    [InlineData(TemplateType.Print)]
    [InlineData(TemplateType.Pdf)]
    public async Task OtherTypes_RenderAsHtml(TemplateType type)
    {
        var template = Template(type, "S", "@Model.V");

        var result = await CreateService().RenderAsync(template, new { V = "a & b" });

        Assert.Equal("a &amp; b", result.Data!.Content);
    }

    [Fact]
    public async Task SmsLayout_IsRenderedAsPlainTextToo()
    {
        var layoutStore = new Mock<ILayoutStoreService>();
        layoutStore
            .Setup(s => s.GetLayoutAsync("SmsWrap", "Notification", "Sms", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new Layout { LayoutName = "SmsWrap", LayoutContent = "[@Model.Brand] @Model.Content" }));
        var service = new TemplateRenderService(_serviceProvider, _engine, new Mock<ITemplateStoreService>().Object, layoutStore.Object);
        var template = Template(TemplateType.Sms, "", "Code @Model.Code & go");
        template.DefaultLayoutName = "SmsWrap";

        var result = await service.RenderAsync(template, new { Code = "1234", Brand = "A&B" });

        Assert.Equal("[A&B] Code 1234 & go", result.Data!.Content);
    }

    [Fact]
    public async Task RenderFromString_Subject_IsNotHtmlEncoded_Body_Is()
    {
        var result = await CreateService().RenderFromStringAsync("<p>@Model.N</p>", new { N = "a & b" }, subjectTemplate: "Hello @Model.N");

        Assert.Equal("Hello a & b", result.Data!.Subject);
        Assert.Equal("<p>a &amp; b</p>", result.Data.Content);
    }

    #endregion

    #region 文件来源：front matter 的 type 要到达实体，否则随包发布的短信模板照样被编码

    [Fact]
    public async Task FileTemplate_TypeFromFrontMatter_ReachesTheEntity()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"Tnzi_OutputKind_{Guid.NewGuid():N}");
        var smsDir = Path.Combine(dir, "Templates", "Notification", "Sms");
        Directory.CreateDirectory(smsDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(smsDir, "Reset.cshtml"),
                "---\nsubject: Reset\nmetadata:\n  type: Sms\n---\nReset here: @Model.Url");
            await File.WriteAllTextAsync(Path.Combine(smsDir, "TopLevel.cshtml"),
                "---\ntype: sms\n---\nx");

            var options = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
            {
                TemplateRootPath = Path.Combine(dir, "Templates"),
                EnableFileSystemTemplates = true,
                TemplateExtension = ".cshtml"
            });
            var fileService = new TemplateFileService(new TemplateFileParser(), options);

            var nested = await fileService.FindTemplateAsync("Reset", "Notification", "Sms");
            var topLevel = await fileService.FindTemplateAsync("TopLevel", "Notification", "Sms");

            Assert.Equal(TemplateType.Sms, nested!.Type);
            Assert.Equal(TemplateType.Sms, topLevel!.Type);

            // 经存储服务投影成实体后类型仍在：这是渲染服务选纯文本路径的唯一依据
            var repository = new Mock<IRepository<TemplateEntity, Guid>>();
            var empty = new List<TemplateEntity>().BuildMock();
            repository.As<IQueryable<TemplateEntity>>().Setup(q => q.Provider).Returns(empty.Provider);
            repository.As<IQueryable<TemplateEntity>>().Setup(q => q.Expression).Returns(empty.Expression);
            repository.As<IQueryable<TemplateEntity>>().Setup(q => q.ElementType).Returns(empty.ElementType);
            repository.As<IQueryable<TemplateEntity>>().Setup(q => q.GetEnumerator()).Returns(() => empty.GetEnumerator());
            var store = new TemplateStoreService(repository.Object, _serviceProvider, fileService);

            var entity = await store.GetTemplateAsync("Reset", "Notification", "Sms");
            Assert.Equal(TemplateType.Sms, entity.Data!.Type);

            var rendered = await new TemplateRenderService(_serviceProvider, _engine, store)
                .RenderByNameAsync("Reset", "Notification", new { Url = "https://x/?a=1&b=2" }, "Sms");
            Assert.Equal("Reset here: https://x/?a=1&b=2", rendered.Data!.Content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    #endregion
}
