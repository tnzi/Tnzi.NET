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

    public async Task<Result<RenderedTemplate>> RenderByNameAsync(string templateName, string module, object? model = null, string? category = null, string? layoutName = null, CancellationToken cancellationToken = default)
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

        return await RenderAsync(templateResult.Data, model, layoutName, cancellationToken);
    }

    public async Task<Result<RenderedTemplate>> RenderAsync(TemplateEntity template, object? model = null, string? layoutName = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(template);

        try
        {
            // 渲染主题（如有）
            var subject = string.Empty;
            if (!string.IsNullOrWhiteSpace(template.SubjectTemplate))
            {
                subject = await _templateEngine.RenderAsync(template.SubjectTemplate, model, cancellationToken);
            }

            // 渲染内容
            var content = string.Empty;
            if (!string.IsNullOrWhiteSpace(template.ContentTemplate))
            {
                content = await _templateEngine.RenderAsync(template.ContentTemplate, model, cancellationToken);
            }

            // 解析布局名称：显式指定 > 模板默认布局
            var effectiveLayoutName = layoutName ?? template.DefaultLayoutName;

            // 应用布局（如有）
            if (!string.IsNullOrWhiteSpace(effectiveLayoutName) && _layoutStoreService != null)
            {
                content = await ApplyLayoutByNameAsync(content, effectiveLayoutName, template.Module, model, cancellationToken);
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

    public async Task<Result<RenderedTemplate>> RenderFromStringAsync(string contentTemplate, object? model = null, string? subjectTemplate = null, string? layoutContent = null, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(contentTemplate);

        try
        {
            // 渲染主题
            var subject = string.Empty;
            if (!string.IsNullOrWhiteSpace(subjectTemplate))
            {
                subject = await _templateEngine.RenderAsync(subjectTemplate, model, cancellationToken);
            }

            // 渲染内容
            var content = await _templateEngine.RenderAsync(contentTemplate, model, cancellationToken);

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

                content = await _templateEngine.RenderAsync(layoutContent, layoutVariables, cancellationToken);
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
    /// 根据布局名称从存储加载布局并应用
    /// </summary>
    private async Task<string> ApplyLayoutByNameAsync(string content, string layoutName, string module, object? model, CancellationToken cancellationToken)
    {
        if (_layoutStoreService == null)
            return content;

        var layoutResult = await _layoutStoreService.GetLayoutAsync(layoutName, module, null, cancellationToken);
        if (!layoutResult.Succeeded || layoutResult.Data == null)
        {
            Logger.LogWarning("Layout '{LayoutName}' not found in module '{Module}', skipping layout application", layoutName, module);
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

        return await _templateEngine.RenderAsync(layout.LayoutContent, layoutVariables, cancellationToken);
    }

    public async Task<Result<PdfRenderResult>> RenderToPdfAsync(string templateName, string module, object? model = null, string? category = null, string? layoutName = null, PdfConvertOptions? pdfOptions = null, CancellationToken cancellationToken = default)
    {
        if (_pdfConverter == null)
            return Fail<PdfRenderResult>("IHtmlToPdfConverter is not registered. Please register an implementation.", 500);

        // 先渲染 HTML
        var renderResult = await RenderByNameAsync(templateName, module, model, category, layoutName, cancellationToken);
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
