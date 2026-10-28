using TemplateEntity = Tnzi.Template.Entities.Template;

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
    private readonly IReadOnlyRepository<TemplateEntity, Guid>? _templateRepository;

    public TemplateCheckRenderer(
        ITemplateRenderService renderService,
        ILogger<TemplateCheckRenderer> logger,
        IReadOnlyRepository<TemplateEntity, Guid>? templateRepository = null)
    {
        _renderService = Check.NotNull(renderService);
        _logger = Check.NotNull(logger);
        // 只用来回答「当前作用域里有没有这一行」（出厂版式兜底的前提）；缺席则不兜底，行为与此前相同。
        _templateRepository = templateRepository;
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

        // 解析走与版式目录、与 CheckService 写快照时**同一处**声明：
        // 模板名留空则按账户版式取出厂默认模板（这正是 CheckLayout 在本渲染路径上生效的地方），
        // 每页张数由解析出来的那份模板声明，而不是由枚举猜。
        var resolution = BuiltInCheckTemplates.Resolve(request.TemplateName, request.Layout);
        var templateName = resolution.TemplateName;

        var model = CheckDocumentModelFactory.Create(request, resolution);

        try
        {
            var rendered = await _renderService.RenderByNameAsync(
                templateName, CheckTemplates.Module, model, CheckTemplates.Category, outputKind: TemplateOutputKind.Html, cancellationToken: cancellationToken);

            if (!rendered.Succeeded && rendered.Code == 404)
                rendered = await RenderShippedBodyAsync(templateName, model, cancellationToken) ?? rendered;

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
            // 模板是用户可编辑内容，失败必须落成可读的 Result 而非 500 堆栈，
            // 且要让 CheckService 的 UoW 走正常的中止回滚路径（回收支票号）。
            // 编译/渲染失败由渲染服务以失败的 Result 返回（上面那一支）；这里兜住它仍会抛出的模板异常（越界路径）。
            _logger.LogError(ex, "Check template '{TemplateName}' failed to render.", templateName);
            return Result<byte[]>.Failure($"Check template '{templateName}' failed to render: {ex.Message}", 500);
        }
    }

    /// <inheritdoc />
    public Task<Result<byte[]>> RenderCalibrationAsync(CheckRenderRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(RenderCalibration(request));

    /// <summary>
    /// 出厂版式在当前作用域里<b>根本没有行</b>时，渲染程序集自带的正文；否则返回 null（保持原失败）。
    /// </summary>
    /// <remarks>
    /// 出厂行由启动播种写成宿主行（<c>TenantId</c> 为空），而多租户的过滤器是严格等值，
    /// 不兜底的话，多租户开启后每个租户的打印、预览、样张、重打全部 404。
    /// <para>
    /// ★ 判据是「没有行」而不是「渲染失败」：库里有一行但被<b>停用</b>，是有人特意关掉了这个版式，
    /// 绕过它就等于让停用失效。租户自己建了同名行之后，那一行照常胜出（上面的按名渲染先命中它）。
    /// 自建模板（不在出厂目录里）缺失是真实的配置错误，不兜底。
    /// </para>
    /// </remarks>
    private async Task<Result<RenderedTemplate>?> RenderShippedBodyAsync(string templateName, CheckDocumentModel model, CancellationToken cancellationToken)
    {
        var builtIn = BuiltInCheckTemplates.Find(templateName);
        if (builtIn == null || _templateRepository == null)
            return null;

        var hasRow = await _templateRepository.AsNoTracking().AnyAsync(
            t => t.TemplateName == builtIn.Name && t.Module == CheckTemplates.Module && t.Category == CheckTemplates.Category,
            cancellationToken);
        if (hasRow)
            return null;

        var content = BuiltInCheckTemplates.ReadContent(builtIn);
        if (content == null)
            return null;

        _logger.LogDebug(
            "No stored row for the built-in check template '{TemplateName}' in the current scope; rendering the body shipped with the assembly.",
            builtIn.Name);

        return await _renderService.RenderAsync(new TemplateEntity
        {
            TemplateName = builtIn.Name,
            Module = CheckTemplates.Module,
            Category = CheckTemplates.Category,
            Type = TemplateType.Print,
            ContentTemplate = content,
            SubjectTemplate = string.Empty,
            IsActive = true,
            Description = builtIn.Description
        }, model, outputKind: TemplateOutputKind.Html, cancellationToken: cancellationToken);
    }
}
