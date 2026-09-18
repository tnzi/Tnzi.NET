namespace Tnzi.Template.Models;

/// <summary>
/// 纯文本输出（<see cref="TemplateOutputKind.PlainText"/>）的模板基类：与 <see cref="TemplateBase"/>
/// 拥有同一套辅助方法，只是两条写入路径都<b>不</b>做 HTML 编码。
/// </summary>
/// <remarks>
/// 邮件主题与短信正文经它编译。<see cref="TemplateBase.Raw"/> / <c>Html.Raw</c> 在这里是空操作 ——
/// 没有编码，也就没有「不编码」这个例外。
/// </remarks>
public abstract class PlainTextTemplateBase : TemplateBase
{
    /// <inheritdoc />
    protected override bool EncodesOutput => false;
}
