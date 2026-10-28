namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 会计核心里由人或调用方填写的字符串字段的列宽，以及超长时的处理。
/// </summary>
/// <remarks>
/// <para>
/// ★ 测试库是 SQLite，不检查列宽：原样赋值的超长值在那里「保存成功」，在 SQL Server / PostgreSQL 上
/// 则是一个不点名的截断 500。所以列宽在服务入口先查一遍，常量由实体配置直接引用，两处不可能漂移。
/// </para>
/// <para>
/// 人填的值<b>拒绝而不截断</b>（悄悄截掉的讨论内容或摘要看起来合法，含义已经变了）；
/// 唯一截断的是过账时<b>由系统拼出来</b>的凭证摘要（<see cref="FitJournalMemo"/>），原文仍完整地留在单据上。
/// </para>
/// </remarks>
internal static class FinanceFieldLimits
{
    /// <summary>讨论与附件的单据键（<c>SourceType</c> / <c>SourceId</c>）列宽</summary>
    internal const int DocumentKeyMaxLength = 64;

    /// <summary><see cref="DocumentComment.Body"/> 列宽</summary>
    internal const int CommentBodyMaxLength = 4000;

    /// <summary><see cref="DocumentAttachment.FileName"/> 列宽</summary>
    internal const int AttachmentFileNameMaxLength = 260;

    /// <summary><see cref="DocumentAttachment.ContentType"/> 列宽</summary>
    internal const int AttachmentContentTypeMaxLength = 128;

    /// <summary><see cref="DocumentAttachment.Caption"/> 列宽</summary>
    internal const int AttachmentCaptionMaxLength = 500;

    /// <summary>单据与凭证摘要（<c>Memo</c>）列宽</summary>
    internal const int MemoMaxLength = 500;

    /// <summary>单据行描述列宽（发票 / 账单 / 贷项 / 费用 / 存款单的行）</summary>
    internal const int LineDescriptionMaxLength = 500;

    /// <summary>收付款 / 转账 / 存款单及存款单行的参考号（<c>Reference</c>）列宽</summary>
    internal const int ReferenceMaxLength = 128;

    /// <summary>收付款与费用单的付款方式（<c>PaymentMethod</c>）列宽</summary>
    internal const int PaymentMethodMaxLength = 32;

    /// <summary>
    /// 依次检查各字段，返回第一个超长字段的拒绝消息（点名字段与上限）；全部合规返回 <see langword="null"/>。
    /// </summary>
    internal static string? FirstTooLong(params (string? Value, int MaxLength, string Field)[] fields)
    {
        foreach (var (value, maxLength, field) in fields)
        {
            if (value != null && value.Length > maxLength)
                return $"The {field} must be at most {maxLength} characters.";
        }
        return null;
    }

    /// <summary>
    /// 把过账时拼出来的凭证摘要收进列宽。
    /// </summary>
    /// <remarks>
    /// 摘要由前缀（"Invoice: "、"Void INV-0001"）加单据摘要、或单据摘要加后缀（" (out)"）拼成，
    /// 单据摘要本身最长就是列宽，所以一份合规的草稿拼出来可以超宽，过账在数据库那一步 500 且永远过不去。
    /// 截掉的只是派生文本：单据上的原文不受影响。
    /// </remarks>
    internal static string? FitJournalMemo(string? memo) => Fit(memo, MemoMaxLength);

    /// <summary>
    /// 把从别处抄过来的文本收进列宽（保留开头）。
    /// </summary>
    /// <remarks>
    /// 只给<b>派生</b>的值用：目录项描述（列宽 1000）缺省成单据行描述（500）、银行流水摘要（512）抄成单据摘要。
    /// 原文仍完整地留在来源上，而操作员没有办法改短一条银行流水；人填的值不走这里，超长拒绝。
    /// 截断点落在代理对中间时整个字符一起舍掉：孤立的高代理项会让严格 UTF-8 的驱动（Npgsql）拒绝写入，过账答 500。
    /// </remarks>
    internal static string? Fit(string? value, int maxLength) => value.FitToLength(maxLength);
}
