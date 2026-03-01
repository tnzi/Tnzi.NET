namespace Tnzi.Template.Models;

/// <summary>
/// 渲染后的模板结果
/// </summary>
public class RenderedTemplate
{
    /// <summary>
    /// 渲染后的主题
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// 渲染后的内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 使用的模板名称
    /// </summary>
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>
    /// 使用的布局名称（如有）
    /// </summary>
    public string? LayoutName { get; set; }
}
