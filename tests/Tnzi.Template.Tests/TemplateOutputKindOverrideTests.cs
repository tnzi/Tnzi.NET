using TemplateEntity = Tnzi.Template.Entities.Template;

namespace Tnzi.Template.Tests;

/// <summary>
/// 渲染的<b>调用方</b>知道出口面（推送正文、纯文本邮件、短信），模板的 <c>Type</c> 只是它自述的用途。
/// 调用方给了 <see cref="TemplateOutputKind"/> 就按调用方的；没给才退回按 <c>Template.Type</c> 推导。
/// </summary>
/// <remarks>
/// <b>被保护的缺陷</b>：纯文本路径只认 <c>Type == Sms</c>，而 <c>TemplateType</c> 没有 Push 成员、
/// 纯文本邮件（<c>IsHtml = false</c>）也不是一种模板类型 —— 推送正文与纯文本邮件正文里的
/// <c>@Model.Url</c> 照样被编码成 <c>&amp;amp;</c>，渲染服务的签名根本不给调用方说话的机会。
/// </remarks>
public class TemplateOutputKindOverrideTests : IDisposable
{
    private readonly RazorTemplateEngine _engine;
    private readonly IMemoryCache _cache;
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<ITemplateStoreService> _storeMock = new();
    private readonly Mock<ILayoutStoreService> _layoutStoreMock = new();

    public TemplateOutputKindOverrideTests()
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

    private TemplateRenderService CreateService(bool withLayouts = false)
        => new(_serviceProvider, _engine, _storeMock.Object, withLayouts ? _layoutStoreMock.Object : null);

    private static readonly object Model = new { Url = "https://x/?token=a&uid=b", Name = "O'Brien" };

    private static TemplateEntity Template(TemplateType type, string content = "Hi @Model.Name: @Model.Url", string subject = "For @Model.Name") => new()
    {
        TemplateName = "T",
        Module = "Notification",
        Category = type.ToString(),
        Type = type,
        SubjectTemplate = subject,
        ContentTemplate = content,
        IsActive = true
    };

    [Fact]
    public async Task GenericTemplate_WithPlainTextOverride_IsNotEncoded()
    {
        // 推送正文：模板类型没有 Push，只能由调用方说「这是纯文本」
        var result = await CreateService().RenderAsync(Template(TemplateType.Generic), Model, outputKind: TemplateOutputKind.PlainText);

        Assert.True(result.Succeeded);
        Assert.Equal("Hi O'Brien: https://x/?token=a&uid=b", result.Data!.Content);
    }

    [Fact]
    public async Task EmailTemplate_WithPlainTextOverride_IsNotEncoded()
    {
        // 纯文本邮件（IsHtml = false）：正文进的是 text/plain 部分
        var result = await CreateService().RenderAsync(Template(TemplateType.Email), Model, outputKind: TemplateOutputKind.PlainText);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain("&amp;", result.Data!.Content);
        Assert.DoesNotContain("&#39;", result.Data.Content);
    }

    [Fact]
    public async Task SmsTemplate_WithHtmlOverride_IsEncoded()
    {
        // 调用方说了算：一份自称短信的模板被放进 HTML 邮件正文时必须编码
        var result = await CreateService().RenderAsync(Template(TemplateType.Sms), Model, outputKind: TemplateOutputKind.Html);

        Assert.True(result.Succeeded);
        Assert.Equal("Hi O&#39;Brien: https://x/?token=a&amp;uid=b", result.Data!.Content);
    }

    [Fact]
    public async Task NoOverride_FallsBackToTemplateType()
    {
        var service = CreateService();

        var sms = await service.RenderAsync(Template(TemplateType.Sms), Model);
        var email = await service.RenderAsync(Template(TemplateType.Email), Model);

        Assert.DoesNotContain("&amp;", sms.Data!.Content);
        Assert.Contains("&amp;", email.Data!.Content);
    }

    [Fact]
    public async Task Subject_StaysPlainText_EvenWithHtmlOverride()
    {
        var result = await CreateService().RenderAsync(Template(TemplateType.Generic), Model, outputKind: TemplateOutputKind.Html);

        Assert.Equal("For O'Brien", result.Data!.Subject);
    }

    [Fact]
    public async Task Layout_FollowsTheOverride()
    {
        _layoutStoreMock
            .Setup(l => l.GetLayoutAsync("L", "Notification", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Layout>.Success(new Layout { LayoutName = "L", Module = "Notification", LayoutContent = "[@Model.Name] @Model.Content", IsActive = true }));

        var result = await CreateService(withLayouts: true).RenderAsync(Template(TemplateType.Generic, content: "@Model.Url"), Model, layoutName: "L", outputKind: TemplateOutputKind.PlainText);

        Assert.True(result.Succeeded);
        Assert.Equal("[O'Brien] https://x/?token=a&uid=b", result.Data!.Content);
    }

    [Fact]
    public async Task RenderByName_PassesTheOverrideThrough()
    {
        _storeMock
            .Setup(s => s.GetTemplateAsync("T", "Notification", "Push", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TemplateEntity>.Success(Template(TemplateType.Generic)));

        var result = await CreateService().RenderByNameAsync("T", "Notification", Model, "Push", outputKind: TemplateOutputKind.PlainText);

        Assert.True(result.Succeeded);
        Assert.Equal("Hi O'Brien: https://x/?token=a&uid=b", result.Data!.Content);
    }

    [Fact]
    public async Task RenderFromString_HonoursTheOutputKind()
    {
        var text = await CreateService().RenderFromStringAsync("@Model.Url", Model, outputKind: TemplateOutputKind.PlainText);
        var html = await CreateService().RenderFromStringAsync("@Model.Url", Model);

        Assert.Equal("https://x/?token=a&uid=b", text.Data!.Content);
        Assert.Equal("https://x/?token=a&amp;uid=b", html.Data!.Content);
    }
}
