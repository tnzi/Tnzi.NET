namespace Tnzi.Template.Models;

/// <summary>
/// Html 助手类，提供 Raw、Encode 等方法
/// </summary>
public class HtmlHelper
{
    private readonly TemplateBase _template;

    public HtmlHelper(TemplateBase template)
    {
        _template = template;
    }

    /// <summary>
    /// 输出原始 HTML（不进行编码）
    /// </summary>
    public object Raw(object? value)
    {
        return _template.Raw(value);
    }

    /// <summary>
    /// HTML 编码。与 <see cref="TemplateBase.HtmlEncode"/> 相同：产物写出时原样输出，不再被编码一次。
    /// </summary>
    public RawContent Encode(object? value)
    {
        return _template.HtmlEncode(value);
    }
}
