namespace Tnzi.Template.Tests;

/// <summary>
/// 文件渲染路径的 YAML front matter 处理。
///
/// 背景：README / docs 把开头的 <c>--- ... ---</c> 块写进了模板文件格式，而引擎的文件渲染路径
/// （RenderFromFileAsync / RenderFromNameAsync）此前把整个文件原样交给 Razor —— 头部不但被打印到
/// 页面上，其中的 <c>@</c> 还会被当作 Razor 表达式编译执行。只有"导入模板存储"那条路径认识它。
/// 这组测试锁定：两条路径对同一个文件得到同一份正文。
/// </summary>
public class FrontMatterRenderingTests : IDisposable
{
    private readonly RazorTemplateEngine _engine;
    private readonly IMemoryCache _cache;
    private readonly string _tempDir;

    public FrontMatterRenderingTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        _tempDir = Path.Combine(Path.GetTempPath(), $"Tnzi_FrontMatter_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _engine = CreateEngine();
    }

    public void Dispose()
    {
        _cache.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private RazorTemplateEngine CreateEngine(string? defaultLayoutPath = null, IMemoryCache? cache = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
        {
            TemplateRootPath = _tempDir,
            EnableCache = true,
            CacheExpirationSeconds = 3600,
            TemplateExtension = ".cshtml",
            EnableHotReload = false,
            DefaultLayoutPath = defaultLayoutPath
        });

        return new RazorTemplateEngine(options, new Mock<ILogger<RazorTemplateEngine>>().Object, cache ?? _cache);
    }

    private async Task<string> WriteTemplateAsync(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content);
        return fullPath;
    }

    /// <summary>文档记载的模板文件格式（与框架自带的 Notification 模板同形）</summary>
    private const string DocumentedTemplate = """
        ---
        subject: Welcome to the site
        layout: DefaultEmail
        description: Welcome email
        metadata:
          type: Email
          version: "1.0"
        ---
        <h2>Welcome @Model.UserName!</h2>
        """;

    #region 头部不得出现在输出里

    [Fact]
    public async Task RenderFromFileAsync_WithFrontMatter_HeaderIsNotEmitted()
    {
        await WriteTemplateAsync("Welcome.cshtml", DocumentedTemplate);

        var result = await _engine.RenderFromFileAsync("Welcome", new { UserName = "Ann" });

        Assert.Equal("<h2>Welcome Ann!</h2>", result);
        Assert.DoesNotContain("---", result);
        Assert.DoesNotContain("subject:", result);
    }

    [Fact]
    public async Task RenderFromNameAsync_WithFrontMatter_HeaderIsNotEmitted()
    {
        await WriteTemplateAsync("Email/Welcome.cshtml", DocumentedTemplate);

        var result = await _engine.RenderFromNameAsync("Email/Welcome", new { UserName = "Ann" });

        Assert.Equal("<h2>Welcome Ann!</h2>", result);
        Assert.DoesNotContain("---", result);
    }

    [Fact]
    public async Task RenderFromFileAsync_LayoutWithFrontMatter_LayoutHeaderIsNotEmitted()
    {
        await WriteTemplateAsync("Body.cshtml", "<p>body</p>");
        await WriteTemplateAsync("Layouts/_Default.cshtml", """
            ---
            description: Default email layout
            isDefault: true
            ---
            <html><body>@Raw(Model.Content)</body></html>
            """);

        var result = await _engine.RenderFromFileAsync("Body", null, "Layouts/_Default");

        Assert.Equal("<html><body><p>body</p></body></html>", result);
        Assert.DoesNotContain("isDefault", result);
    }

    [Fact]
    public async Task RenderFromFileAsync_DefaultLayoutWithFrontMatter_LayoutHeaderIsNotEmitted()
    {
        await WriteTemplateAsync("Body2.cshtml", "<p>body</p>");
        await WriteTemplateAsync("Layouts/_Fallback.cshtml", """
            ---
            description: Fallback layout
            ---
            <html>@Raw(Model.Content)</html>
            """);

        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var engine = CreateEngine("Layouts/_Fallback", cache);

        var result = await engine.RenderFromFileAsync("Body2");

        Assert.Equal("<html><p>body</p></html>", result);
        Assert.DoesNotContain("description:", result);
    }

    #endregion

    #region 头部不得被当作 Razor 编译执行

    /// <summary>
    /// 决定性用例：front matter 里出现 <c>@</c>（邮箱是最常见的形态）。
    /// 剥离之前，这个文件根本编译不过 —— 说明头部不只是"被打印出来"，它是被当作模板代码执行的。
    /// </summary>
    [Fact]
    public async Task RenderFromFileAsync_FrontMatterContainingAtSign_DoesNotBreakCompilation()
    {
        await WriteTemplateAsync("Contact.cshtml", """
            ---
            subject: Your receipt
            description: Reply to support@example.com for help
            ---
            <p>Hello @Model.UserName</p>
            """);

        var result = await _engine.RenderFromFileAsync("Contact", new { UserName = "Ann" });

        Assert.Equal("<p>Hello Ann</p>", result);
    }

    /// <summary>
    /// 头部里的 <c>@Model.X</c> 此前会被真的求值（README 的 <c>subject: Welcome to @Model.SiteName!</c>
    /// 就是这个形态），渲染出来的头部里带着真实数据。剥离后它连出现的机会都没有。
    /// </summary>
    [Fact]
    public async Task RenderFromFileAsync_FrontMatterWithRazorExpression_IsNotEvaluated()
    {
        await WriteTemplateAsync("Interpolated.cshtml", """
            ---
            subject: Welcome to @Model.SiteName!
            ---
            <p>@Model.UserName</p>
            """);

        var result = await _engine.RenderFromFileAsync("Interpolated", new { UserName = "Ann", SiteName = "Tnzi" });

        Assert.Equal("<p>Ann</p>", result);
        Assert.DoesNotContain("Welcome to", result);
    }

    #endregion

    #region 无 front matter 的文件行为不变

    [Fact]
    public async Task RenderFromFileAsync_WithoutFrontMatter_OutputUnchanged()
    {
        const string content = "<h1>Title</h1>\n<p>Hello @Model.Name</p>";
        await WriteTemplateAsync("Plain.cshtml", content);

        var result = await _engine.RenderFromFileAsync("Plain", new { Name = "Ann" });

        Assert.Equal("<h1>Title</h1>\n<p>Hello Ann</p>", result);
    }

    /// <summary>正文中间出现的 <c>---</c>（分隔线）不是 front matter，不得被吃掉。</summary>
    [Fact]
    public async Task RenderFromFileAsync_WithDashesInBody_KeepsThem()
    {
        const string content = "<p>before</p>\n---\n<p>after</p>\n---\n<p>end</p>";
        await WriteTemplateAsync("Dashes.cshtml", content);

        var result = await _engine.RenderFromFileAsync("Dashes");

        Assert.Equal(content, result);
    }

    [Fact]
    public async Task RenderFromFileAsync_LayoutWithoutFrontMatter_OutputUnchanged()
    {
        await WriteTemplateAsync("Body3.cshtml", "<p>body</p>");
        await WriteTemplateAsync("Layouts/_Plain.cshtml", "<html>@Raw(Model.Content)</html>");

        var result = await _engine.RenderFromFileAsync("Body3", null, "Layouts/_Plain");

        Assert.Equal("<html><p>body</p></html>", result);
    }

    [Fact]
    public async Task RenderFromFileAsync_FrontMatterOnly_RendersEmpty()
    {
        await WriteTemplateAsync("HeaderOnly.cshtml", """
            ---
            subject: Nothing but metadata
            ---
            """);

        var result = await _engine.RenderFromFileAsync("HeaderOnly");

        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region 与模板存储路径一致

    /// <summary>
    /// "导入存储后渲染"与"直接按文件渲染"必须得到同一份正文。
    /// 两条路径各写一套 front matter 判定就会在这里裂开。
    /// </summary>
    [Fact]
    public async Task EngineAndStoreParser_ProduceTheSameBody()
    {
        var fullPath = await WriteTemplateAsync("Shared.cshtml", DocumentedTemplate);

        var parsed = await new TemplateFileParser().ParseTemplateFileAsync(fullPath);
        var parsedRendered = await _engine.RenderAsync(parsed!.ContentTemplate, new { UserName = "Ann" });
        var fileRendered = await _engine.RenderFromFileAsync("Shared", new { UserName = "Ann" });

        Assert.Equal(parsedRendered, fileRendered);
    }

    #endregion

    #region 元数据刻意不注入模型

    /// <summary>
    /// front matter 是**文件格式**，不是模型数据：引擎剥离它，不解析、不注入 @Model。
    /// 理由有三：① 存储渲染路径（TemplateRenderService）同样不把 Subject 交给布局，注入会让两条
    /// 路径反向不一致；② front matter 里的 subject 本身是一段 Razor 源码（<c>Welcome to @Model.SiteName!</c>），
    /// 原样注入等于把未渲染的模板字符串打到页面上；③ 会静默覆盖调用方模型里的同名字段。
    /// 需要元数据的消费方应走 ITemplateStoreService / ITemplateRenderService。
    /// </summary>
    [Fact]
    public async Task RenderFromFileAsync_FrontMatterMetadata_IsNotInjectedIntoModel()
    {
        await WriteTemplateAsync("Meta.cshtml", """
            ---
            subject: From front matter
            ---
            <p>[@Model.Subject]</p>
            """);

        var result = await _engine.RenderFromFileAsync("Meta", new { UserName = "Ann" });

        Assert.Equal("<p>[]</p>", result);
    }

    /// <summary>调用方模型里的同名字段不受 front matter 影响。</summary>
    [Fact]
    public async Task RenderFromFileAsync_ModelWinsOverFrontMatterKey()
    {
        await WriteTemplateAsync("Shadow.cshtml", """
            ---
            subject: From front matter
            ---
            <p>@Model.Subject</p>
            """);

        var result = await _engine.RenderFromFileAsync("Shadow", new { Subject = "From caller" });

        Assert.Equal("<p>From caller</p>", result);
    }

    #endregion

    #region 预编译与热重载

    /// <summary>
    /// 预编译按"内容哈希"建缓存键。若预编译读的是带头部的原文而渲染读的是剥离后的正文，
    /// 两个键对不上 —— 预编译白做且无人察觉。
    /// </summary>
    [Fact]
    public async Task PrecompileTemplatesAsync_WithFrontMatter_ThenRenders()
    {
        await WriteTemplateAsync("Pre.cshtml", DocumentedTemplate);

        await _engine.PrecompileTemplatesAsync(["Pre.cshtml"]);
        var result = await _engine.RenderFromFileAsync("Pre", new { UserName = "Ann" });

        Assert.Equal("<h2>Welcome Ann!</h2>", result);
    }

    [Fact]
    public async Task RenderFromFileAsync_HotReload_StripsFrontMatterAfterEdit()
    {
        var fullPath = await WriteTemplateAsync("Hot.cshtml", """
            ---
            subject: v1
            ---
            <p>v1</p>
            """);

        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var options = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
        {
            TemplateRootPath = _tempDir,
            EnableCache = true,
            CacheExpirationSeconds = 3600,
            TemplateExtension = ".cshtml",
            EnableHotReload = true
        });
        var engine = new RazorTemplateEngine(options, new Mock<ILogger<RazorTemplateEngine>>().Object, cache);

        var first = await engine.RenderFromFileAsync("Hot");
        Assert.Equal("<p>v1</p>", first);

        await File.WriteAllTextAsync(fullPath, """
            ---
            subject: v2
            ---
            <p>v2</p>
            """);
        File.SetLastWriteTimeUtc(fullPath, DateTime.UtcNow.AddSeconds(1));

        var second = await engine.RenderFromFileAsync("Hot");
        Assert.Equal("<p>v2</p>", second);
    }

    #endregion
}
