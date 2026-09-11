namespace Tnzi.Signing.Metadata;

/// <summary>
/// 匿名端点收进来的两个自由文本字段的上限。
/// </summary>
/// <remarks>
/// <para>
/// <c>Signer.SignatureImage</c>（签名图 data URL）与 <c>ConsentText</c>（同意条款原文）是
/// <b>匿名可写</b>的字段：收件人不是系统用户，写入这两列的路径不要求任何登录。此前它们是
/// 列上无上限、服务里不校验的自由文本 —— 一个匿名可写的无界字段就是存储滥用面。
/// 上限同时写在列（<c>HasMaxLength</c>）与服务（<c>SubmitAsync</c> 前置校验）上：
/// 列上限保证越界写不进去，服务校验保证越界的请求得到一句可读的 400 而不是数据库异常。
/// </para>
/// <para>
/// 取值的依据：签名板导出的 PNG 通常 10 到 60 KB，base64 后 ×4/3；高分屏上的大画布可到 200 KB 出头。
/// 512 KiB 给了三倍余量，仍然远低于「能塞进一份合同的任何附件」的量级。同意条款是一段展示给
/// 签署人看的文本，4000 字符与本模块其它长文本列同量级。
/// </para>
/// </remarks>
public static class SigningLimits
{
    /// <summary>签名图（data URL 或裸 base64）的最大字符数。</summary>
    public const int MaxSignatureImageLength = 512 * 1024;

    /// <summary>同意条款原文的最大字符数。</summary>
    public const int MaxConsentTextLength = 4000;
}
