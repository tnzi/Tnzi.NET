namespace Tnzi.Template.Models;

/// <summary>
/// 一次渲染的输出是什么：决定 <c>@expression</c> 的值要不要 HTML 编码。
/// </summary>
/// <remarks>
/// 编码归属于<b>出口</b>而不是引擎。邮件正文、打印件、页面是 HTML，模型值必须编码
/// （谁能建一个往来方，谁就能决定别人打开那份文档时浏览器执行什么）；
/// 邮件主题、短信正文是纯文本，编码只会把 <c>O'Brien</c> 变成 <c>O&amp;#39;Brien</c>、
/// 把短信里的 <c>?token=x&amp;uid=y</c> 变成坏链接，且全程无异常、无日志。
/// </remarks>
public enum TemplateOutputKind
{
    /// <summary>HTML 输出：<c>@expr</c> 编码，只有 <c>Raw</c> / <c>Html.Raw</c> 原样输出。</summary>
    Html = 0,

    /// <summary>纯文本输出：<c>@expr</c> 原样输出；<c>Raw</c> 在这种输出下是空操作。</summary>
    PlainText = 1,
}
