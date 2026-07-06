namespace Tnzi.Finance.Seeding;

/// <summary>
/// 默认通用科目表模板（最小可用集）
/// </summary>
/// <remarks>
/// 消费应用可忽略此模板自建科目表，或在此基础上扩展；
/// 系统科目通过 SystemRole 标记，报表与过账管线按角色解析，不依赖具体编码。
/// 科目名称为面向用户的数据，使用英文。
/// </remarks>
public static class DefaultChartOfAccounts
{
    /// <summary>
    /// 模板科目定义
    /// </summary>
    public sealed record TemplateAccount(
        string Code,
        string Name,
        AccountRootType RootType,
        bool IsGroup = false,
        string? ParentCode = null,
        string? SubType = null,
        AccountSystemRole? SystemRole = null,
        CashFlowActivity? CashFlowActivity = null);

    /// <summary>
    /// 默认模板（父节点必须先于子节点出现）
    /// </summary>
    public static IReadOnlyList<TemplateAccount> Template { get; } =
    [
        new("1000", "Assets", AccountRootType.Asset, IsGroup: true),
        new("1100", "Cash and Cash Equivalents", AccountRootType.Asset, IsGroup: true, ParentCode: "1000"),
        new("1110", "Cash on Hand", AccountRootType.Asset, ParentCode: "1100", SubType: "Cash"),
        new("1120", "Bank Account", AccountRootType.Asset, ParentCode: "1100", SubType: "Bank"),
        new("1130", "Undeposited Funds", AccountRootType.Asset, ParentCode: "1100",
            SystemRole: AccountSystemRole.UndepositedFunds),
        new("1200", "Accounts Receivable", AccountRootType.Asset, ParentCode: "1000", SubType: "Receivable",
            SystemRole: AccountSystemRole.AccountsReceivable),
        new("1300", "Tax Receivable", AccountRootType.Asset, ParentCode: "1000", SubType: "Tax",
            SystemRole: AccountSystemRole.TaxReceivable),
        new("1400", "Inventory", AccountRootType.Asset, ParentCode: "1000", SubType: "Inventory"),
        new("1500", "Fixed Assets", AccountRootType.Asset, ParentCode: "1000", SubType: "FixedAsset"),

        new("2000", "Liabilities", AccountRootType.Liability, IsGroup: true),
        new("2100", "Accounts Payable", AccountRootType.Liability, ParentCode: "2000", SubType: "Payable",
            SystemRole: AccountSystemRole.AccountsPayable),
        new("2200", "Tax Payable", AccountRootType.Liability, ParentCode: "2000", SubType: "Tax",
            SystemRole: AccountSystemRole.TaxPayable),
        new("2300", "Accrued Liabilities", AccountRootType.Liability, ParentCode: "2000"),

        new("3000", "Equity", AccountRootType.Equity, IsGroup: true),
        new("3100", "Owner's Equity", AccountRootType.Equity, ParentCode: "3000"),
        new("3200", "Retained Earnings", AccountRootType.Equity, ParentCode: "3000",
            SystemRole: AccountSystemRole.RetainedEarnings),
        new("3300", "Opening Balance Equity", AccountRootType.Equity, ParentCode: "3000",
            SystemRole: AccountSystemRole.OpeningBalance),

        new("4000", "Income", AccountRootType.Income, IsGroup: true),
        new("4100", "Sales Revenue", AccountRootType.Income, ParentCode: "4000"),
        new("4900", "Other Income", AccountRootType.Income, ParentCode: "4000"),

        new("5000", "Expenses", AccountRootType.Expense, IsGroup: true),
        new("5100", "Cost of Goods Sold", AccountRootType.Expense, ParentCode: "5000"),
        new("5200", "Operating Expenses", AccountRootType.Expense, ParentCode: "5000"),
        new("5300", "Payroll Expenses", AccountRootType.Expense, ParentCode: "5000"),
        new("5800", "Exchange Gain/Loss", AccountRootType.Expense, ParentCode: "5000",
            SystemRole: AccountSystemRole.ExchangeGainLoss),
        new("5900", "Rounding Differences", AccountRootType.Expense, ParentCode: "5000",
            SystemRole: AccountSystemRole.RoundingDifference)
    ];
}
