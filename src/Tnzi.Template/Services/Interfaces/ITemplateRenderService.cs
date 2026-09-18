using TemplateEntity = Tnzi.Template.Entities.Template;

namespace Tnzi.Template.Services;

/// <summary>
/// 模板渲染服务接口（高级 API）
/// 统一编排 ITemplateEngine + ITemplateStoreService + ILayoutStoreService，
/// 提供从数据库加载模板、渲染主题和内容、应用布局的一站式渲染能力
/// </summary>
/// <remarks>
/// <para>
/// <b>正文的输出类型由调用方决定，模板类型只是退路。</b>渲染的调用方知道出口面（HTML 邮件、纯文本邮件、
/// 短信、推送），模板的 <c>Type</c> 只是它自述的用途：<c>TemplateType</c> 没有 Push 成员，纯文本邮件也不是
/// 一种模板类型。<c>outputKind</c> 传 <c>null</c> 时按 <c>Template.Type</c> 推导（<c>Sms</c> = 纯文本，其余 HTML）；
/// 传了值就按传的。主题永远按纯文本渲染，不受它影响。
/// </para>
/// </remarks>
public interface ITemplateRenderService
{
    /// <summary>
    /// 根据模板名称渲染（从数据库加载模板，渲染主题和内容，自动应用布局）
    /// </summary>
    /// <param name="templateName">模板名称</param>
    /// <param name="module">所属模块</param>
    /// <param name="model">模板变量</param>
    /// <param name="category">模板分类（可选）</param>
    /// <param name="layoutName">布局名称（可选，优先于模板默认布局）</param>
    /// <param name="outputKind">正文（及其布局）的输出类型；<c>null</c> = 按 <c>Template.Type</c> 推导</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>渲染结果（包含 Subject 和 Content）</returns>
    Task<Result<RenderedTemplate>> RenderByNameAsync(string templateName, string module, object? model = null, string? category = null, string? layoutName = null, TemplateOutputKind? outputKind = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据模板实体渲染（已有模板实体时，跳过数据库查询，直接渲染）
    /// </summary>
    /// <param name="template">模板实体</param>
    /// <param name="model">模板变量</param>
    /// <param name="layoutName">布局名称（可选，优先于模板默认布局）</param>
    /// <param name="outputKind">正文（及其布局）的输出类型；<c>null</c> = 按 <c>Template.Type</c> 推导</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>渲染结果</returns>
    Task<Result<RenderedTemplate>> RenderAsync(TemplateEntity template, object? model = null, string? layoutName = null, TemplateOutputKind? outputKind = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从字符串内容渲染（不涉及数据库，仅使用引擎渲染）
    /// </summary>
    /// <param name="contentTemplate">内容模板</param>
    /// <param name="model">模板变量</param>
    /// <param name="subjectTemplate">主题模板（可选）</param>
    /// <param name="layoutContent">布局内容（可选，直接传入布局字符串）</param>
    /// <param name="outputKind">正文（及其布局）的输出类型；没有模板实体可退回，默认 HTML</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>渲染结果</returns>
    Task<Result<RenderedTemplate>> RenderFromStringAsync(string contentTemplate, object? model = null, string? subjectTemplate = null, string? layoutContent = null, TemplateOutputKind outputKind = TemplateOutputKind.Html, CancellationToken cancellationToken = default);

    /// <summary>
    /// 渲染模板并转换为 PDF 字节（便捷方法）
    /// 先通过模板名称渲染 HTML，再通过 IHtmlToPdfConverter 转换为 PDF
    /// </summary>
    /// <param name="templateName">模板名称</param>
    /// <param name="module">所属模块</param>
    /// <param name="model">模板变量</param>
    /// <param name="category">模板分类（可选）</param>
    /// <param name="layoutName">布局名称（可选）</param>
    /// <param name="pdfOptions">PDF 转换选项（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>PDF 转换结果</returns>
    Task<Result<PdfRenderResult>> RenderToPdfAsync(string templateName, string module, object? model = null, string? category = null, string? layoutName = null, PdfConvertOptions? pdfOptions = null, CancellationToken cancellationToken = default);
}
