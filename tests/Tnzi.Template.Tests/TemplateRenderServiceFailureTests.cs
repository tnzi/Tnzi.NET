using TemplateEntity = Tnzi.Template.Entities.Template;

namespace Tnzi.Template.Tests;

/// <summary>
/// <see cref="ITemplateRenderService"/> 的方法返回 <see cref="Result{T}"/>：模板编译失败、渲染期出错都要落成失败的 Result，
/// 而不是从服务里抛出去。
/// </summary>
/// <remarks>
/// <b>被保护的缺陷</b>：引擎把每一种失败包成 <see cref="TemplateException"/> 的子类，渲染服务却对它原样重抛，
/// 返回 Result 的约定只覆盖了「模板不存在」。调用方按约定写的退路于是只在模板缺失时生效：
/// 通知带了原始主题与正文、模板里一处空引用，创建通知直接抛 500 而不是按文档回落到原始内容；
/// 发票渲染的内置 HTML 兜底同样走不到。
/// </remarks>
public class TemplateRenderServiceFailureTests : IDisposable
{
    private readonly RazorTemplateEngine _engine;
    private readonly IMemoryCache _cache;
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<ITemplateStoreService> _storeMock = new();

    // 运行期出错：缺失的字段经 SafeDynamicObject 返回 null，再取成员就是一次空引用绑定
    private const string RuntimeErrorContent = "Items: @Model.Items.Count";

    // 编译期出错：Razor 代码块里的 C# 语法错误
    private const string CompileErrorContent = "@{ var x = ; }";

    public TemplateRenderServiceFailureTests()
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

    private TemplateRenderService CreateService(ITemplateEngine? engine = null, IHtmlToPdfConverter? pdfConverter = null)
        => new(_serviceProvider, engine ?? _engine, _storeMock.Object, pdfConverter: pdfConverter);

    private static TemplateEntity Template(string content, string subject = "Hello") => new()
    {
        TemplateName = "Broken",
        Module = "Notification",
        Category = "Email",
        Type = TemplateType.Email,
        SubjectTemplate = subject,
        ContentTemplate = content,
        IsActive = true
    };

    [Fact]
    public async Task RenderAsync_RuntimeError_ReturnsFailedResult()
    {
        var result = await CreateService().RenderAsync(Template(RuntimeErrorContent), new { Name = "x" });

        Assert.False(result.Succeeded);
        Assert.Equal(500, result.Code);
        Assert.Equal(ErrorCodes.TEMPLATE_RENDER_ERROR, result.ErrorCode);
        Assert.Contains("Broken", result.Message);
    }

    [Fact]
    public async Task RenderAsync_CompileError_ReturnsFailedResult_WithTheCompilationCode()
    {
        var result = await CreateService().RenderAsync(Template(CompileErrorContent));

        Assert.False(result.Succeeded);
        Assert.Equal(500, result.Code);
        Assert.Equal(ErrorCodes.TEMPLATE_COMPILATION_ERROR, result.ErrorCode);
    }

    /// <summary>主题是另一次渲染，它出错同样落成失败的 Result。</summary>
    [Fact]
    public async Task RenderAsync_SubjectError_ReturnsFailedResult()
    {
        var result = await CreateService().RenderAsync(Template("Body", subject: RuntimeErrorContent));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.TEMPLATE_RENDER_ERROR, result.ErrorCode);
    }

    [Fact]
    public async Task RenderByNameAsync_RuntimeError_ReturnsFailedResult()
    {
        _storeMock
            .Setup(s => s.GetTemplateAsync("Broken", "Notification", "Email", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TemplateEntity>.Success(Template(RuntimeErrorContent)));

        var result = await CreateService().RenderByNameAsync("Broken", "Notification", new { Name = "x" }, "Email");

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.TEMPLATE_RENDER_ERROR, result.ErrorCode);
    }

    [Theory]
    [InlineData(RuntimeErrorContent, ErrorCodes.TEMPLATE_RENDER_ERROR)]
    [InlineData(CompileErrorContent, ErrorCodes.TEMPLATE_COMPILATION_ERROR)]
    public async Task RenderFromStringAsync_EngineError_ReturnsFailedResult(string content, string expectedErrorCode)
    {
        var result = await CreateService().RenderFromStringAsync(content, new { Name = "x" });

        Assert.False(result.Succeeded);
        Assert.Equal(500, result.Code);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
    }

    /// <summary>PDF 便捷方法建在按名渲染之上：渲染失败就停在这里，转换器不被调用。</summary>
    [Fact]
    public async Task RenderToPdfAsync_RenderError_ReturnsFailedResult_WithoutConverting()
    {
        _storeMock
            .Setup(s => s.GetTemplateAsync("Broken", "Payment", "Invoice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TemplateEntity>.Success(Template(RuntimeErrorContent)));
        var converter = new Mock<IHtmlToPdfConverter>();

        var result = await CreateService(pdfConverter: converter.Object).RenderToPdfAsync("Broken", "Payment", new { Name = "x" }, "Invoice");

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.TEMPLATE_RENDER_ERROR, result.ErrorCode);
        converter.Verify(c => c.ConvertAsync(It.IsAny<string>(), It.IsAny<PdfConvertOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// 越界路径不是模板内容的问题而是调用方或配置的问题，照旧抛出（403），不混进「模板渲染失败」让调用方回落。
    /// </summary>
    [Fact]
    public async Task RenderAsync_SecurityException_StillThrows()
    {
        var engine = new Mock<ITemplateEngine>();
        engine
            .Setup(e => e.RenderAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<TemplateOutputKind>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TemplateSecurityException("Access denied", "../x"));

        await Assert.ThrowsAsync<TemplateSecurityException>(() => CreateService(engine.Object).RenderAsync(Template("Body")));
        await Assert.ThrowsAsync<TemplateSecurityException>(() => CreateService(engine.Object).RenderFromStringAsync("Body"));
    }

    /// <summary>
    /// 调用方取消的那一次不是模板失败：照旧抛出，免得调用方按「渲染失败」走回落、带着已取消的令牌继续往下做。
    /// 引擎把取消包成了 <see cref="TemplateRenderException"/>，所以按令牌判，不按异常类型判。
    /// </summary>
    [Fact]
    public async Task RenderAsync_CancelledCall_Throws_InsteadOfReportingARenderFailure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var engine = new Mock<ITemplateEngine>();
        engine
            .Setup(e => e.RenderAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<TemplateOutputKind>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TemplateRenderException("Template rendering failed: cancelled", new OperationCanceledException(cts.Token)));

        await Assert.ThrowsAsync<TemplateRenderException>(() => CreateService(engine.Object).RenderAsync(Template("Body"), cancellationToken: cts.Token));
        await Assert.ThrowsAsync<TemplateRenderException>(() => CreateService(engine.Object).RenderFromStringAsync("Body", cancellationToken: cts.Token));
    }
}
