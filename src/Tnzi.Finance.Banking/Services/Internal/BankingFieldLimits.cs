namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// 银行域里人手工填写的字符串字段的列宽，以及超长时的拒绝消息。
/// </summary>
/// <remarks>
/// <para>
/// ★ 这些值由操作员敲进来（档案名、开户行、备注、作废原因、规则名与条件值……），
/// 原样赋给实体时，超过列宽的值在 SQL Server / PostgreSQL 上让保存抛截断异常，界面上只剩一个
/// 不点名的 500；测试库是 SQLite，不检查列宽，所以这类缺陷在测试里永远是绿的。
/// </para>
/// <para>
/// ★ 人打的值<b>拒绝而不截断</b>（同 <see cref="ReceiptFieldLimits"/>）：悄悄截掉的作废原因、
/// 规则条件值，看起来合法，含义却已经变了。
/// </para>
/// <para>
/// 常量由各实体配置直接引用，两处不可能漂移。
/// </para>
/// </remarks>
internal static class BankingFieldLimits
{
    /// <summary><see cref="BankAccount.Name"/> 列宽</summary>
    internal const int BankAccountNameMaxLength = 128;

    /// <summary><see cref="BankAccount.BankName"/> 与 <see cref="PartyBankAccount.BankName"/> 列宽</summary>
    internal const int BankNameMaxLength = 128;

    /// <summary>币种列宽（银行账户档案、往来方账户、支票）</summary>
    internal const int CurrencyMaxLength = 8;

    /// <summary><see cref="BankAccount.CheckTemplateName"/> 列宽（支票快照的 <c>PrintTemplateName</c> 同宽）</summary>
    internal const int CheckTemplateNameMaxLength = 200;

    /// <summary><see cref="BankAccount.FeedProviderKey"/> 列宽</summary>
    internal const int FeedProviderKeyMaxLength = 64;

    /// <summary><see cref="BankAccount.ExternalAccountId"/> 列宽</summary>
    internal const int ExternalAccountIdMaxLength = 128;

    /// <summary><see cref="BankAccount.EftOriginatorName"/> 列宽</summary>
    internal const int EftOriginatorNameMaxLength = 64;

    /// <summary><see cref="PartyBankAccount.Label"/> 列宽</summary>
    internal const int PartyBankLabelMaxLength = 64;

    /// <summary><see cref="PartyBankAccount.Notes"/> 列宽</summary>
    internal const int PartyBankNotesMaxLength = 500;

    /// <summary><see cref="BankCheck.PayeeName"/> 列宽</summary>
    internal const int CheckPayeeNameMaxLength = 256;

    /// <summary>作废 / 毁票原因列宽（<see cref="BankCheck.VoidReason"/> 与 <see cref="EftBatch.VoidReason"/>）</summary>
    internal const int VoidReasonMaxLength = 256;

    /// <summary><see cref="BankRule.Name"/> 列宽</summary>
    internal const int BankRuleNameMaxLength = 200;

    /// <summary><see cref="BankRuleCondition.Value"/> 列宽</summary>
    internal const int BankRuleConditionValueMaxLength = 500;

    /// <summary><see cref="BankRule.PaymentMethod"/> 列宽</summary>
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
}
