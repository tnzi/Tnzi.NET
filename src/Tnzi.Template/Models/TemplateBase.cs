namespace Tnzi.Template.Models;

/// <summary>
/// 自定义 Razor 模板基类
/// 提供 Raw、HtmlEncode 等常用辅助方法，以及常用类型的访问
/// </summary>
public abstract class TemplateBase : RazorEngineTemplateBase
{
    /// <summary>
    /// Html 助手对象，提供 Html.Raw() 等方法
    /// </summary>
    public HtmlHelper Html => new(this);

    /// <summary>
    /// 输出原始 HTML（不进行编码）
    /// 使用方法：@Html.Raw(Model.HtmlContent) 或 @Html.Raw("<strong>Bold</strong>")
    /// </summary>
    public object Raw(object? value)
    {
        return new RawContent(value?.ToString() ?? string.Empty);
    }

    /// <summary>
    /// HTML 编码
    /// </summary>
    public string HtmlEncode(object? value)
    {
        if (value == null) return string.Empty;
        return System.Net.WebUtility.HtmlEncode(value.ToString() ?? string.Empty);
    }

    /// <summary>
    /// URL 编码
    /// </summary>
    public string UrlEncode(object? value)
    {
        if (value == null) return string.Empty;
        return System.Net.WebUtility.UrlEncode(value.ToString() ?? string.Empty);
    }

    /// <summary>
    /// 格式化日期
    /// </summary>
    public string FormatDate(DateTime? date, string format = "yyyy-MM-dd")
    {
        return date?.ToString(format) ?? string.Empty;
    }

    /// <summary>
    /// 格式化日期时间
    /// </summary>
    public string FormatDateTime(DateTime? date, string format = "yyyy-MM-dd HH:mm:ss")
    {
        return date?.ToString(format) ?? string.Empty;
    }

    /// <summary>
    /// 获取当前时间
    /// </summary>
    public DateTime Now => DateTime.Now;

    /// <summary>
    /// 获取当前 UTC 时间
    /// </summary>
    public DateTime UtcNow => DateTime.UtcNow;

    /// <summary>
    /// 获取今天日期
    /// </summary>
    public DateTime Today => DateTime.Today;

    /// <summary>
    /// 条件输出
    /// </summary>
    public string If(bool condition, string trueValue, string falseValue = "")
    {
        return condition ? trueValue : falseValue;
    }

    /// <summary>
    /// 截断文本
    /// </summary>
    public string Truncate(string? value, int maxLength, string suffix = "...")
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value ?? string.Empty;

        return value[..maxLength] + suffix;
    }
}

/// <summary>
/// 带泛型模型的模板基类
/// </summary>
/// <typeparam name="T">模型类型</typeparam>
public abstract class TemplateBase<T> : TemplateBase
{
    /// <summary>
    /// 强类型模型
    /// </summary>
    public new T Model { get; set; } = default!;
}

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
    /// HTML 编码
    /// </summary>
    public string Encode(object? value)
    {
        return _template.HtmlEncode(value);
    }
}

/// <summary>
/// 原始内容包装器（避免 HTML 编码）
/// </summary>
public class RawContent
{
    private readonly string _value;

    public RawContent(string value)
    {
        _value = value;
    }

    public override string ToString()
    {
        return _value;
    }
}
