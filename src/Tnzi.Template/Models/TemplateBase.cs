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
    /// 写出一个 <c>@expression</c> 的值：默认 <b>HTML 编码</b>，
    /// 只有 <see cref="RawContent"/>（即 <see cref="Raw"/> / <c>Html.Raw</c> 的产物）原样输出。
    /// </summary>
    /// <remarks>
    /// ★ 基类直接把值 Append 进输出，于是模板里的每一个 <c>@Model.X</c> 都是原样注入 HTML。
    /// 模板的数据来源是业务记录里的自由文本（往来方名称、摘要、消费方自定义的行），
    /// 谁能建一个往来方，谁就能决定渲染出来的那份文档里有什么标签。
    /// 这个类从一开始就提供了 <see cref="Raw"/>、<see cref="HtmlEncode"/> 与
    /// <see cref="RawContent"/> —— 而 <see cref="RawContent"/> <b>全仓没有一个消费点</b>，
    /// 说明「默认编码、显式 Raw 例外」本就是设计意图，只是这一半从未接上。
    /// <para>
    /// 与 ASP.NET Core Razor 的语义一致：<c>@expr</c> 编码，<c>@Html.Raw(expr)</c> 不编码。
    /// 刻意输出 HTML 的模板（邮件正文）因此必须改用 <c>Raw</c>。
    /// </para>
    /// </remarks>
    public override void Write(object? obj)
    {
        if (obj is null)
            return;
        if (obj is RawContent)
        {
            base.Write(obj);
            return;
        }
        base.Write(HtmlEncode(obj));
    }

    /// <summary>
    /// 写出一个属性值（<c>attr="@expr"</c>）：与 <see cref="Write"/> 同口径。
    /// </summary>
    /// <remarks>
    /// 属性位置比元素内容更好利用（一个引号加一个 <c>onerror=</c> 就够了），
    /// 而 Razor 走的是这条单独的路径，所以两处必须一起编码，
    /// 漏掉哪一处都等于没编码。字面量部分（模板作者自己写的）原样输出。
    /// </remarks>
    public override void WriteAttributeValue(string prefix, int prefixOffset, object? value, int valueOffset, int valueLength, bool isLiteral)
    {
        if (isLiteral || value is RawContent)
        {
            base.WriteAttributeValue(prefix, prefixOffset, value, valueOffset, valueLength, isLiteral);
            return;
        }
        base.WriteAttributeValue(prefix, prefixOffset, HtmlEncode(value), valueOffset, valueLength, isLiteral);
    }

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
