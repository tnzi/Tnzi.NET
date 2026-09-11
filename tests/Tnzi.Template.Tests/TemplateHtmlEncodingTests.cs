namespace Tnzi.Template.Tests;

/// <summary>
/// 模板输出的 HTML 转义：<c>@expr</c> 一律编码，只有 <c>Raw</c> / <c>Html.Raw</c> 例外。
/// </summary>
/// <remarks>
/// 模板的数据来源是业务记录里的自由文本（往来方名称、单据摘要、消费应用自己填的行）。
/// 原样注入的后果不是排版难看：能建一个往来方的人，就能决定别人打开那份文档时
/// 浏览器执行什么。此前 <c>RawContent</c> 与 <c>Raw()</c> 都在，却没有一个消费点 ——
/// 「默认编码、显式 Raw 例外」是设计意图，只是从未接上。
/// </remarks>
public class TemplateHtmlEncodingTests : IDisposable
{
    private readonly RazorTemplateEngine _engine;
    private readonly IMemoryCache _cache;
    private readonly string _tempDir;

    public TemplateHtmlEncodingTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        _tempDir = Path.Combine(Path.GetTempPath(), $"Tnzi_Template_Encoding_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var options = Microsoft.Extensions.Options.Options.Create(new TemplateOptions
        {
            TemplateRootPath = _tempDir,
            EnableCache = false,
            TemplateExtension = ".cshtml",
            EnableHotReload = false
        });
        _engine = new RazorTemplateEngine(options, new Mock<ILogger<RazorTemplateEngine>>().Object, _cache);
    }

    public void Dispose()
    {
        _cache.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { /* 临时目录清理失败不影响断言 */ }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ModelValue_InElementContent_IsHtmlEncoded()
    {
        var result = await _engine.RenderAsync(
            "<p>@Model.PayeeName</p>",
            new { PayeeName = "<script>alert(1)</script>" });

        Assert.DoesNotContain("<script>", result);
        Assert.Contains("&lt;script&gt;", result);
    }

    /// <summary>
    /// ★ 属性位置走的是另一条写入路径（<c>WriteAttributeValue</c>）。
    /// 只挡住元素内容而漏掉属性等于没挡：一个引号加一个 <c>onerror=</c> 就够了。
    /// </summary>
    [Fact]
    public async Task ModelValue_InAttribute_IsHtmlEncoded()
    {
        var result = await _engine.RenderAsync(
            "<img alt=\"@Model.Caption\" src=\"x\" />",
            new { Caption = "\" onerror=\"alert(1)" });

        Assert.DoesNotContain("onerror=\"alert(1)\"", result);
        Assert.Contains("&quot;", result);
    }

    /// <summary>刻意输出 HTML 的模板（邮件正文）仍然走得通 —— 这是 Raw 存在的理由。</summary>
    [Fact]
    public async Task RawContent_IsWrittenThrough()
    {
        var result = await _engine.RenderAsync(
            "<div>@Raw(Model.Body)</div>",
            new { Body = "<strong>Bold</strong>" });

        Assert.Contains("<strong>Bold</strong>", result);
    }

    [Fact]
    public async Task HtmlRaw_IsWrittenThrough()
    {
        var result = await _engine.RenderAsync(
            "<div>@Html.Raw(Model.Body)</div>",
            new { Body = "<em>Emphasis</em>" });

        Assert.Contains("<em>Emphasis</em>", result);
    }

    /// <summary>普通文本不该因为转义而改样（&amp; 之外的字符逐字保留）。</summary>
    [Fact]
    public async Task OrdinaryText_IsUnchanged()
    {
        var result = await _engine.RenderAsync("<p>@Model.Name</p>", new { Name = "Acme Supplies Ltd." });
        Assert.Equal("<p>Acme Supplies Ltd.</p>", result);
    }

    /// <summary>模板作者自己写的标签是字面量，不受影响。</summary>
    [Fact]
    public async Task LiteralMarkup_IsNotEncoded()
    {
        var result = await _engine.RenderAsync("<p><b>@Model.Name</b></p>", new { Name = "x" });
        Assert.Equal("<p><b>x</b></p>", result);
    }
}
