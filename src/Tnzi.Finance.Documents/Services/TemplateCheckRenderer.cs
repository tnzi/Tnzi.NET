namespace Tnzi.Finance.Documents.Services;

/// <summary>
/// 模板驱动的支票渲染器（默认实现）：渲染入库的 Razor 支票模板 → HTML
/// </summary>
/// <remarks>
/// 版式不再是硬编码坐标，而是 <c>Tnzi.Template</c> 里一条可经模板管理端编辑的
/// <c>TemplateType.Print</c> 模板（内置 <see cref="CheckTemplates.Cpa006Canada"/> 由
/// <see cref="Internal.CheckTemplateSeeder"/> 幂等播种）：不同项目/银行用不同模板、
/// 微调毫米坐标无需改代码、无需发版。
///
/// 输出 <b>HTML</b>（<see cref="ContentType"/> = <c>text/html</c>）：
/// <list type="bullet">
/// <item>所见即所得预览——呈现端 iframe 直出，预印票纸的预印元素在屏幕上照常可见；</item>
/// <item>打印走浏览器 <c>@media print</c>，预印元素 <c>visibility:hidden</c> 只留占位，
///       服务端不引入 PDF 引擎依赖。</item>
/// </list>
/// 需要真 PDF（尤其白纸票纸的 E-13B 磁码字形）时，消费应用注册
/// <see cref="PdfSharpCheckRenderer"/> 覆盖本实现即可（子模块已把它注册为具体类型）。
/// </remarks>
public class TemplateCheckRenderer : ICheckDocumentRenderer
{
    private readonly ITemplateRenderService _renderService;
    private readonly ILogger<TemplateCheckRenderer> _logger;

    public TemplateCheckRenderer(ITemplateRenderService renderService, ILogger<TemplateCheckRenderer> logger)
    {
        _renderService = Check.NotNull(renderService);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public string ContentType => "text/html";

    /// <inheritdoc />
    public string FileExtension => ".html";

    /// <summary>
    /// 同步渲染（仅为满足契约的既有同步调用方；框架内的 <c>CheckService</c> 走
    /// <see cref="RenderAsync"/>）。模板加载与 Razor 编译天然异步，此处阻塞等待。
    /// </summary>
    public Result<byte[]> Render(CheckRenderRequest request)
        => RenderAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Result<byte[]> RenderCalibration(CheckRenderRequest request)
    {
        Check.NotNull(request);
        return Result<byte[]>.Success(Encoding.UTF8.GetBytes(CheckCalibrationSheet.Build(request)));
    }

    /// <inheritdoc />
    public async Task<Result<byte[]>> RenderAsync(CheckRenderRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);
        if (request.Checks.Count == 0)
            return Result<byte[]>.Failure("No checks to render.", 400);

        var templateName = string.IsNullOrWhiteSpace(request.TemplateName)
            ? CheckTemplates.DefaultName
            : request.TemplateName.Trim();

        var model = CheckDocumentModelFactory.Create(request);

        try
        {
            var rendered = await _renderService.RenderByNameAsync(
                templateName, CheckTemplates.Module, model, CheckTemplates.Category, cancellationToken: cancellationToken);

            if (!rendered.Succeeded || rendered.Data == null)
            {
                // 模板缺失/停用是配置问题，把模板坐标带进消息，运维不必翻日志。
                return Result<byte[]>.Failure(
                    $"Check template '{templateName}' (module '{CheckTemplates.Module}', category '{CheckTemplates.Category}') could not be rendered: {rendered.Message}",
                    rendered.Code ?? 500);
            }

            return Result<byte[]>.Success(Encoding.UTF8.GetBytes(rendered.Data.Content));
        }
        catch (TemplateException ex)
        {
            // 模板是用户可编辑内容，编译/渲染失败必须落成可读的 Result 而非 500 堆栈，
            // 且要让 CheckService 的 UoW 走正常的中止回滚路径（回收支票号）。
            _logger.LogError(ex, "Check template '{TemplateName}' failed to render.", templateName);
            return Result<byte[]>.Failure($"Check template '{templateName}' failed to render: {ex.Message}", 500);
        }
    }

    /// <inheritdoc />
    public Task<Result<byte[]>> RenderCalibrationAsync(CheckRenderRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(RenderCalibration(request));
}
