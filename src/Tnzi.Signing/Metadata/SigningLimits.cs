namespace Tnzi.Signing.Metadata;

/// <summary>
/// 自由文本列的上限：匿名端点收进来的，以及发起方（管理端 / 程序调用）写入的。
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

    /// <summary>签名图的最大边长（像素，宽与高各自不超过它）。</summary>
    /// <remarks>
    /// 字符数上限管不住解码后的内存：一张高度压缩的 PNG 不到 400 KB 就能声明 18000 × 18000 像素，
    /// 密封时整张解码要分配上 GB。所以提交时按文件头里声明的尺寸拦（见 <see cref="MaxSignatureImagePixels"/>），
    /// 早于任何解码。签名板的画布在高分屏上通常一两千像素宽，这里给了数倍余量。
    /// </remarks>
    public const int MaxSignatureImageSide = 4096;

    /// <summary>签名图的最大像素数（宽 × 高）。理由见 <see cref="MaxSignatureImageSide"/>。</summary>
    public const int MaxSignatureImagePixels = 4 * 1024 * 1024;

    /// <summary>同意条款原文的最大字符数。</summary>
    public const int MaxConsentTextLength = 4000;

    /// <summary>拒签原因的最大字符数。</summary>
    public const int MaxDeclineReasonLength = 1000;

    /// <summary>单个字段取值的最大字符数。</summary>
    public const int MaxFieldValueLength = 4000;

    /// <summary>记在签署审计里的 User-Agent 的最大字符数（越界截断，不拒绝）。</summary>
    public const int MaxSignerUserAgentLength = 512;

    // ── 发起方（管理端 / 程序调用）写入的列 ──────────────────────────────────────────
    // 同样是列与服务共用一个常量：发起时越界按字段指名 400，早于任何落库与排版。
    // 此前只校验了值长度，标题、收件人、预填键越界在 SQL Server / PostgreSQL 上是一次 500，
    // 而请求行已经先落了库，留下一份没有收件人的草稿。

    /// <summary>签署请求标题（<c>Envelope.Title</c>）。</summary>
    public const int MaxTitleLength = 200;

    /// <summary>宿主类型名（<c>Envelope.HostEntityType</c>）。</summary>
    public const int MaxHostEntityTypeLength = 60;

    /// <summary>收件人角色（<c>Signer.Role</c>，与字段的 <c>RecipientRole</c> 同宽）。</summary>
    public const int MaxRoleLength = 60;

    /// <summary>收件人姓名（<c>Signer.Name</c>）。</summary>
    public const int MaxSignerNameLength = 160;

    /// <summary>收件人邮箱（<c>Signer.Email</c>）。</summary>
    public const int MaxEmailLength = 256;

    /// <summary>字段键（<c>FieldValue.FieldKey</c>，与模板字段的 <c>Key</c> 同宽）。</summary>
    public const int MaxFieldKeyLength = 80;

    /// <summary>
    /// 有效期上限（天），与管理端表单同一个范围。有效期从<b>发出</b>那一刻起算。
    /// 没有上限时一个很大的值会让计算截止时间抛 <c>ArgumentOutOfRangeException</c>（500）。
    /// </summary>
    public const int MaxExpiresInDays = 365;

    /// <summary>把取证类文本裁到列宽之内；<c>null</c> 原样返回。</summary>
    /// <remarks>
    /// 截断点落在代理对中间时整个字符一起舍掉：孤立的高代理项会让严格 UTF-8 的驱动（Npgsql）拒绝写入，
    /// 那一次签署或拒签就答 500。
    /// </remarks>
    public static string? Fit(string? value, int maxLength) => value.FitToLength(maxLength);

    /// <summary>
    /// 由请求标题派生的文件名主干（非法字符换成下划线），最多 <paramref name="maxLength"/> 个码元。
    /// </summary>
    /// <remarks>
    /// 文件名长度在各文件系统上限不同，调用方按自己的后缀留出余量。截断点落在代理对中间时整个字符一起舍掉：
    /// 文件名随文件记录落库，孤立的高代理项会让严格 UTF-8 的驱动（Npgsql）拒绝写入，标题里一个 emoji 就让渲染或密封失败。
    /// </remarks>
    internal static string FileNameStem(string? title, int maxLength)
    {
        var safe = string.IsNullOrWhiteSpace(title) ? "document" : title.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '_');
        return safe.FitToLength(maxLength);
    }
}
