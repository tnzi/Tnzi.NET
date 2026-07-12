namespace Tnzi.Finance.Metadata;

/// <summary>
/// 科目根类型（会计五要素）
/// </summary>
public enum AccountRootType
{
    /// <summary>资产（正常余额方向：借方）</summary>
    Asset = 1,

    /// <summary>负债（正常余额方向：贷方）</summary>
    Liability = 2,

    /// <summary>权益（正常余额方向：贷方）</summary>
    Equity = 3,

    /// <summary>收入（正常余额方向：贷方）</summary>
    Income = 4,

    /// <summary>费用（正常余额方向：借方）</summary>
    Expense = 5
}

/// <summary>
/// 系统科目角色
/// 框架及消费应用按角色解析系统科目，而非硬编码科目编码，
/// 使科目表模板可自由映射（每租户每角色至多一个科目）
/// </summary>
public enum AccountSystemRole
{
    /// <summary>应收账款控制科目</summary>
    AccountsReceivable = 1,

    /// <summary>应付账款控制科目</summary>
    AccountsPayable = 2,

    /// <summary>销项税/应交税费科目</summary>
    TaxPayable = 3,

    /// <summary>进项税/待抵扣税科目</summary>
    TaxReceivable = 4,

    /// <summary>留存收益科目</summary>
    RetainedEarnings = 5,

    /// <summary>汇兑损益科目</summary>
    ExchangeGainLoss = 6,

    /// <summary>舍入差额科目（过账自动配平尾差）</summary>
    RoundingDifference = 7,

    /// <summary>待存款项科目（已收未存银行）</summary>
    UndepositedFunds = 8,

    /// <summary>期初余额过渡科目</summary>
    OpeningBalance = 9
}

/// <summary>
/// 现金流量表活动分类（挂在科目上，供现金流量报表归类）
/// </summary>
/// <remarks>
/// 未分类（null）的资产负债类科目在现金流量表中落入显式的 Unclassified 桶；
/// 收入/费用类科目的分类被报表忽略（其净额整体经净利润进入经营活动）。
/// </remarks>
public enum CashFlowActivity
{
    /// <summary>经营活动</summary>
    Operating = 1,

    /// <summary>投资活动</summary>
    Investing = 2,

    /// <summary>筹资活动</summary>
    Financing = 3,

    /// <summary>
    /// 现金及现金等价物——现金流量表的解释对象本身，不参与活动分桶；
    /// 报表的期初/期末现金与现金净变动按携带此分类的科目聚合
    /// </summary>
    CashEquivalent = 4
}

/// <summary>
/// 目录项类型（P2 无库存数量流转，仅目录 + 默认科目）
/// </summary>
public enum ItemType
{
    /// <summary>服务</summary>
    Service = 1,

    /// <summary>商品（非库存）</summary>
    Product = 2
}

/// <summary>
/// 业务单据状态（Invoice/Bill/Expense/CreditMemo/PaymentEntry 通用）
/// </summary>
public enum FinanceDocumentStatus
{
    /// <summary>草稿（可编辑、可删除）</summary>
    Draft = 0,

    /// <summary>已过账（不可变；A/R、A/P 单据即"未清"）</summary>
    Posted = 1,

    /// <summary>部分核销（P2c 结算派生）</summary>
    PartiallyPaid = 2,

    /// <summary>已核销/已付清（P2c 结算派生）</summary>
    Paid = 3,

    /// <summary>已作废（过账凭证已冲销）</summary>
    Voided = 4
}

/// <summary>
/// 收付款方向
/// </summary>
public enum PaymentDirection
{
    /// <summary>收款（客户 → 我方）</summary>
    Inbound = 1,

    /// <summary>付款（我方 → 供应商）</summary>
    Outbound = 2
}

/// <summary>
/// 财务往来方类型
/// </summary>
public enum FinancePartyType
{
    /// <summary>客户</summary>
    Customer = 1,

    /// <summary>供应商</summary>
    Vendor = 2
}

/// <summary>
/// 结算单据类型（核销的源与目标）
/// </summary>
public enum SettlementDocType
{
    /// <summary>销售发票（核销目标）</summary>
    Invoice = 1,

    /// <summary>采购账单（核销目标）</summary>
    Bill = 2,

    /// <summary>收付款单（核销源）</summary>
    PaymentEntry = 3,

    /// <summary>销售贷项单（核销源，抵减发票）</summary>
    CreditMemo = 4
}

/// <summary>
/// 过账前钩子拦截的操作类型（见 IFinancePostingGuard）
/// </summary>
public enum FinancePostingOperation
{
    /// <summary>过账（业务单据或凭证 Draft → Posted）</summary>
    Post = 1,

    /// <summary>作废（业务单据 Posted → Voided，冲销过账凭证）</summary>
    Void = 2,

    /// <summary>冲销（凭证 Posted → Reversed）</summary>
    Reverse = 3
}

/// <summary>
/// 银行对账状态
/// </summary>
public enum ReconciliationStatus
{
    /// <summary>草稿（可勾选/撤销行、可编辑、可删除）</summary>
    Draft = 0,

    /// <summary>已完成（锁定：勾选与头字段均不可再改）</summary>
    Completed = 1
}

/// <summary>
/// 会计凭证状态
/// </summary>
public enum JournalEntryStatus
{
    /// <summary>草稿（可编辑、可删除）</summary>
    Draft = 0,

    /// <summary>已过账（不可变，修正须冲销）</summary>
    Posted = 1,

    /// <summary>已冲销（原凭证保留在账中，由冲销凭证抵消）</summary>
    Reversed = 2
}
