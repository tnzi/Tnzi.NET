namespace Tnzi.Signing.Metadata;

/// <summary>
/// 匿名端点收进来的自由文本的上限。
/// </summary>
/// <remarks>
/// <para>
/// 收件人不是系统用户，写入这些列的路径不要求任何登录 —— 一个匿名可写的无界字段就是存储滥用面。
/// 上限同时写在列（<c>HasMaxLength</c>）与服务上：列上限保证越界写不进去，服务侧保证越界的请求
/// 得到一句可读的 400（或被截断）而不是数据库异常。SQLite 不强制 varchar 宽度，所以列越界只在
/// SQL Server / PostgreSQL / MySQL 上出现，症状是 500；<c>SubmitSigningPayloadTests</c> 用 EF 模型
/// 比对这里的每一个常量防漂。
/// </para>
/// <para>
/// 两种处理方式对应两种来源：<b>收件人填的</b>（签名图 / 同意条款 / 拒签原因 / 字段值）越界拒绝，
/// 他能改；<b>浏览器自报的</b>（User-Agent）越界截断，它是取证数据，一个合法但很长的 WebView UA
/// 不该让人签不了字。
/// </para>
/// <para>
/// 取值的依据：签名板导出的 PNG 通常 10 到 60 KB，base64 后 ×4/3；高分屏上的大画布可到 200 KB 出头。
/// 512 KiB 给了三倍余量，仍然远低于「能塞进一份合同的任何附件」的量级。同意条款是一段展示给
/// 签署人看的文本，字段值会被画进成品的一个框里，两者 4000 字符与本模块其它长文本列同量级。
/// </para>
/// </remarks>
public static class SigningLimits
{
    /// <summary>签名图（data URL 或裸 base64）的最大字符数。</summary>
    public const int MaxSignatureImageLength = 512 * 1024;

    /// <summary>同意条款原文的最大字符数。</summary>
    public const int MaxConsentTextLength = 4000;

    /// <summary>拒签原因的最大字符数。</summary>
    public const int MaxDeclineReasonLength = 1000;

    /// <summary>单个字段取值的最大字符数。</summary>
    public const int MaxFieldValueLength = 4000;

    /// <summary>记在签署审计里的 User-Agent 的最大字符数（越界截断，不拒绝）。</summary>
    public const int MaxSignerUserAgentLength = 512;

    /// <summary>把取证类文本裁到列宽之内；<c>null</c> 原样返回。</summary>
    public static string? Fit(string? value, int maxLength)
        => value is { Length: > 0 } && value.Length > maxLength ? value[..maxLength] : value;
}
