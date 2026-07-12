namespace Tnzi.Finance.Metadata;

/// <summary>
/// 结算方式推荐取值常量
/// </summary>
/// <remarks>
/// <see cref="Entities.PaymentEntry.PaymentMethod"/> / <see cref="Entities.Expense.PaymentMethod"/>
/// 为自由字符串字段（各辖区结算工具差异大，枚举会过死），本类仅提供通用推荐值；
/// 消费应用可直接使用自定义值（如 "EFT"、"Alipay"）。
/// </remarks>
public static class PaymentMethods
{
    /// <summary>现金</summary>
    public const string Cash = "Cash";

    /// <summary>支票</summary>
    public const string Check = "Check";

    /// <summary>信用卡</summary>
    public const string CreditCard = "CreditCard";

    /// <summary>借记卡</summary>
    public const string DebitCard = "DebitCard";

    /// <summary>银行转账</summary>
    public const string BankTransfer = "BankTransfer";

    /// <summary>电汇</summary>
    public const string Wire = "Wire";

    /// <summary>其他</summary>
    public const string Other = "Other";
}
