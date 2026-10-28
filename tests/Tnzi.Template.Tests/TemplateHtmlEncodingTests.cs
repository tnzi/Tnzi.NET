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

    /// <summary>
    /// ★ <c>@expr</c> 默认编码之后，显式的 <c>HtmlEncode</c> / <c>Html.Encode</c> 若仍返回普通字符串，
    /// 写出时会再被编码一次：<c>A &amp; B</c> 渲染成 <c>A &amp;amp; B</c>，邮件与 PDF 里看得见，无异常。
    /// 两个助手的产物已经是编码过的文本，写出时必须原样输出。
    /// </summary>
    [Theory]
    [InlineData("<p>@HtmlEncode(Model.Name)</p>")]
    [InlineData("<p>@Html.Encode(Model.Name)</p>")]
    public async Task ExplicitEncodeHelpers_AreEncodedExactlyOnce(string template)
    {
        var result = await _engine.RenderAsync(template, new { Name = "A & B <C>" });

        Assert.Equal("<p>A &amp; B &lt;C&gt;</p>", result);
    }

    [Fact]
    public async Task ExplicitEncodeHelper_InAttribute_IsEncodedExactlyOnce()
    {
        var result = await _engine.RenderAsync("<a title=\"@HtmlEncode(Model.Name)\">x</a>", new { Name = "A & \"B\"" });

        Assert.Equal("<a title=\"A &amp; &quot;B&quot;\">x</a>", result);
    }

    /// <summary>
    /// 两个助手的产物仍能当字符串用：拼进 <c>Raw(...)</c> 的字符串、赋给 <c>string</c> 变量、
    /// 传给收 <c>string</c> 的助手。模型是 dynamic，这些调用在运行期绑定，换返回类型时最容易在这里断掉。
    /// </summary>
    [Fact]
    public async Task ExplicitEncodeHelper_StillComposesAsAString()
    {
        var result = await _engine.RenderAsync(
            "@Raw(\"<b>\" + HtmlEncode(Model.Name) + \"</b>\")|@{ string s = Html.Encode(Model.Name); }@Raw(s)|@Raw(Truncate(HtmlEncode(Model.Name), 5))",
            new { Name = "A & B" });

        Assert.Equal("<b>A &amp; B</b>|A &amp; B|A &am...", result);
    }

    /// <summary>纯文本输出下 <c>@expr</c> 不编码，显式 <c>HtmlEncode</c> 照旧编码一次：调用方要的就是编码。</summary>
    [Fact]
    public async Task ExplicitEncodeHelper_InPlainText_EncodesOnce()
    {
        var result = await _engine.RenderAsync("@HtmlEncode(Model.Name)", new { Name = "A & B" }, TemplateOutputKind.PlainText);

        Assert.Equal("A &amp; B", result);
    }
}
