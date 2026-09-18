using TemplateEntity = Tnzi.Template.Entities.Template;

namespace Tnzi.Template.Services;

/// <summary>
/// 模板渲染服务实现
/// 统一编排 ITemplateEngine + ITemplateStoreService + ILayoutStoreService
/// </summary>
public class TemplateRenderService : ApplicationService, ITemplateRenderService
{
    private readonly ITemplateEngine _templateEngine;
    private readonly ITemplateStoreService _templateStoreService;
    private readonly ILayoutStoreService? _layoutStoreService;
    private readonly IHtmlToPdfConverter? _pdfConverter;

    public TemplateRenderService(
        IServiceProvider serviceProvider,
        ITemplateEngine templateEngine,
        ITemplateStoreService templateStoreService,
        ILayoutStoreService? layoutStoreService = null,
        IHtmlToPdfConverter? pdfConverter = null)
        : base(serviceProvider)
    {
        _templateEngine = Check.NotNull(templateEngine);
        _templateStoreService = Check.NotNull(templateStoreService);
        _layoutStoreService = layoutStoreService;
        _pdfConverter = pdfConverter;
    }

    public async Task<Result<RenderedTemplate>> RenderByNameAsync(string templateName, string module, object? model = null, string? category = null, string? layoutName = null, TemplateOutputKind? outputKind = null, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(templateName);
        Check.NotNullOrWhiteSpace(module);

        // 从存储加载模板
        var templateResult = await _templateStoreService.GetTemplateAsync(templateName, module, category, cancellationToken);
        if (!templateResult.Succeeded || templateResult.Data == null)
        {
            return Fail<RenderedTemplate>(
                templateResult.Message ?? $"Template '{templateName}' not found in module '{module}'",
                templateResult.Code ?? 404,
                templateResult.ErrorCode);
        }

        return await RenderAsync(templateResult.Data, model, layoutName, outputKind, cancellationToken);
    }

    public async Task<Result<RenderedTemplate>> RenderAsync(TemplateEntity template, object? model = null, string? layoutName = null, TemplateOutputKind? outputKind = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(template);

        try
        {
            // 渲染主题（如有）。主题永远是纯文本：它进的是邮件头，不是 HTML 文档
            var subject = string.Empty;
            if (!string.IsNullOrWhiteSpace(template.SubjectTemplate))
            {
                subject = await _templateEngine.RenderAsync(template.SubjectTemplate, model, TemplateOutputKind.PlainText, cancellationToken);
            }

            // 渲染内容：调用方说了出口就按调用方的，没说才按模板类型推导
            var contentKind = outputKind ?? ContentOutputKind(template.Type);
            var content = string.Empty;
            if (!string.IsNullOrWhiteSpace(template.ContentTemplate))
            {
                content = await _templateEngine.RenderAsync(template.ContentTemplate, model, contentKind, cancellationToken);
            }

            // 解析布局名称：显式指定 > 模板默认布局
            var effectiveLayoutName = layoutName ?? template.DefaultLayoutName;

            // 应用布局（如有），布局与正文同一种输出
            if (!string.IsNullOrWhiteSpace(effectiveLayoutName) && _layoutStoreService != null)
            {
                content = await ApplyLayoutByNameAsync(content, effectiveLayoutName, template.Module, template.Category, model, contentKind, cancellationToken);
            }

            return Ok(new RenderedTemplate
            {
                Subject = subject,
                Content = content,
                TemplateName = template.TemplateName,
                LayoutName = effectiveLayoutName
            });
        }
        catch (TemplateException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to render template '{TemplateName}' in module '{Module}'", template.TemplateName, template.Module);
            return Fail<RenderedTemplate>($"Failed to render template '{template.TemplateName}': {ex.Message}", 500, ErrorCodes.TEMPLATE_RENDER_ERROR);
        }
    }

    public async Task<Result<RenderedTemplate>> RenderFromStringAsync(string contentTemplate, object? model = null, string? subjectTemplate = null, string? layoutContent = null, TemplateOutputKind outputKind = TemplateOutputKind.Html, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(contentTemplate);

        try
        {
            // 渲染主题（纯文本，见 RenderAsync）
            var subject = string.Empty;
            if (!string.IsNullOrWhiteSpace(subjectTemplate))
            {
                subject = await _templateEngine.RenderAsync(subjectTemplate, model, TemplateOutputKind.PlainText, cancellationToken);
            }

            // 渲染内容（没有模板实体可退回，输出类型完全由调用方给）
            var content = await _templateEngine.RenderAsync(contentTemplate, model, outputKind, cancellationToken);

            // 应用布局（直接使用布局字符串）
            if (!string.IsNullOrWhiteSpace(layoutContent))
            {
                var layoutVariables = new Dictionary<string, object?>
                {
                    ["Content"] = content
                };

                // 合并原始模型变量到布局变量中
                if (model != null)
                {
                    MergeModelToLayoutVariables(model, layoutVariables);
                }

                content = await _templateEngine.RenderAsync(layoutContent, layoutVariables, outputKind, cancellationToken);
            }

            return Ok(new RenderedTemplate
            {
                Subject = subject,
                Content = content
            });
        }
        catch (TemplateException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to render template from string");
            return Fail<RenderedTemplate>($"Failed to render template: {ex.Message}", 500, ErrorCodes.TEMPLATE_RENDER_ERROR);
        }
    }

    /// <summary>
    /// 调用方没说出口时，按模板类型推导正文的输出类型：短信是纯文本，其余（邮件正文、页面、打印件、PDF、未指定）都是 HTML。
    /// </summary>
    /// <remarks>
    /// 编码归属于出口而不是引擎。2026-09-04 起引擎默认对 <c>@expr</c> 做 HTML 编码（邮件正文与打印件必须如此），
    /// 但短信正文经同一个引擎渲染，插一条带查询串的链接就会变成 <c>?token=x&amp;amp;uid=y</c>：
    /// 坏链接、无异常、发送状态成功。传真仍按 HTML：它经浏览器渲染成 PDF。
    /// 这只是退路：推送正文、纯文本邮件都不是一种模板类型，知道出口面的是调用方，它经 <c>outputKind</c> 直说。
    /// </remarks>
    private static TemplateOutputKind ContentOutputKind(TemplateType type)
        => type == TemplateType.Sms ? TemplateOutputKind.PlainText : TemplateOutputKind.Html;

    /// <summary>
    /// 根据布局名称从存储加载布局并应用
    /// </summary>
    private async Task<string> ApplyLayoutByNameAsync(string content, string layoutName, string module, string? category, object? model, TemplateOutputKind outputKind, CancellationToken cancellationToken)
    {
        if (_layoutStoreService == null)
            return content;

        // 先按模板自身的分类查找。文件系统布局按分类分目录存放
        // （Templates/Layouts/{category}/_{name}.cshtml，框架内置的邮件布局即
        // Layouts/Email/_DefaultEmail.cshtml），此前这里固定传 null，拼出的路径是
        // Layouts/_DefaultEmail.cshtml —— 内置布局永远命中不了，所有走内置模板的邮件
        // 都丢掉了布局外壳（页眉/页脚），只发出内容片段。
        var layoutResult = await _layoutStoreService.GetLayoutAsync(layoutName, module, category, cancellationToken);

        // 回退到不带分类的查找：数据库里的布局行分类可能为空，或布局直接放在
        // Layouts/ 根下，两种组织方式都要能用。
        if ((!layoutResult.Succeeded || layoutResult.Data == null) && !string.IsNullOrWhiteSpace(category))
        {
            layoutResult = await _layoutStoreService.GetLayoutAsync(layoutName, module, null, cancellationToken);
        }

        if (!layoutResult.Succeeded || layoutResult.Data == null)
        {
            Logger.LogWarning("Layout '{LayoutName}' not found in module '{Module}' (category: {Category}), skipping layout application", layoutName, module, category);
            return content;
        }

        var layout = layoutResult.Data;
        var layoutVariables = new Dictionary<string, object?>
        {
            ["Content"] = content
        };

        // 合并原始模型变量到布局变量中（布局可能需要访问标题、日期等信息）
        if (model != null)
        {
            MergeModelToLayoutVariables(model, layoutVariables);
        }

        return await _templateEngine.RenderAsync(layout.LayoutContent, layoutVariables, outputKind, cancellationToken);
    }

    public async Task<Result<PdfRenderResult>> RenderToPdfAsync(string templateName, string module, object? model = null, string? category = null, string? layoutName = null, PdfConvertOptions? pdfOptions = null, CancellationToken cancellationToken = default)
    {
        if (_pdfConverter == null)
            return Fail<PdfRenderResult>("IHtmlToPdfConverter is not registered. Please register an implementation.", 500);

        // 先渲染 HTML
        // PDF 经浏览器出：正文恒为 HTML
        var renderResult = await RenderByNameAsync(templateName, module, model, category, layoutName, TemplateOutputKind.Html, cancellationToken);
        if (!renderResult.Succeeded)
            return Fail<PdfRenderResult>(renderResult.Message ?? "Template rendering failed", renderResult.Code ?? 500, renderResult.ErrorCode);

        try
        {
            // 通过转换器将 HTML 转换为 PDF
            var pdfBytes = await _pdfConverter.ConvertAsync(renderResult.Data!.Content, pdfOptions, cancellationToken);

            return Ok(new PdfRenderResult
            {
                Content = pdfBytes,
                ContentType = _pdfConverter.ContentType,
                FileExtension = _pdfConverter.FileExtension,
                TemplateName = renderResult.Data.TemplateName
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to convert HTML to PDF for template '{TemplateName}'", templateName);
            return Fail<PdfRenderResult>($"PDF conversion failed: {ex.Message}", 500);
        }
    }

    /// <summary>
    /// 将模型变量合并到布局变量字典中
    /// </summary>
    private static void MergeModelToLayoutVariables(object model, Dictionary<string, object?> layoutVariables)
    {
        if (model is IDictionary<string, object> dict)
        {
            foreach (var kvp in dict)
            {
                layoutVariables.TryAdd(kvp.Key, kvp.Value);
            }
        }
        else if (model is IDictionary<string, object?> nullableDict)
        {
            foreach (var kvp in nullableDict)
            {
                layoutVariables.TryAdd(kvp.Key, kvp.Value);
            }
        }
        else
        {
            // 反射读取对象属性
            foreach (var prop in model.GetType().GetProperties())
            {
                layoutVariables.TryAdd(prop.Name, prop.GetValue(model));
            }
        }
    }
}
