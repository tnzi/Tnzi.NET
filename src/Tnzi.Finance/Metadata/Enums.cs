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
    OpeningBalance = 9,

    /// <summary>换汇过渡科目（跨币种划转的两侧资金行经此科目在同工作单元内精确归零）</summary>
    CurrencyExchangeClearing = 10,

    /// <summary>应付工资科目（薪酬批次过账贷记员工净额，付款时借记冲平）</summary>
    WagesPayable = 11,

    /// <summary>不可抵扣采购税费用科目（税码 IsRecoverable=false 时采购税作为成本过入此科目，而非 TaxReceivable）</summary>
    NonRecoverableTaxExpense = 12
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

/// <summary>
/// 银行账号方案（路由号编码规范，决定校验规则与 MICR 行拼装）
/// </summary>
public enum BankNumberScheme
{
    /// <summary>美国 ABA 路由号（9 位，含 mod-10 校验位）</summary>
    UsAba = 1,

    /// <summary>加拿大 EFT（机构号 3 位 + 分行 transit 号 5 位）</summary>
    CaEft = 2
}

/// <summary>
/// 支票票纸类型
/// </summary>
public enum CheckStockType
{
    /// <summary>预印票纸（默认，MICR 行已预印，打印时不再打 MICR）</summary>
    PrePrinted = 1,

    /// <summary>白纸全打印（须配置 E-13B MICR 字体路径）</summary>
    Blank = 2
}

/// <summary>
/// 支票版式
/// </summary>
public enum CheckLayout
{
    /// <summary>支票 + 两联存根（Voucher）</summary>
    Voucher = 1,

    /// <summary>每页三张支票</summary>
    ThreePerPage = 2
}

/// <summary>
/// 支票状态（占号留痕：三种状态均占用支票号，无物理删除）
/// </summary>
public enum CheckStatus
{
    /// <summary>已开具（已分配号、已打印或登记）</summary>
    Issued = 1,

    /// <summary>已作废（票据无效，号码仍占用留痕）</summary>
    Void = 2,

    /// <summary>已毁票（打印损坏/对齐失败的空白票，号码占用留痕，无关联付款）</summary>
    Spoiled = 3
}

/// <summary>
/// EFT 文件格式（决定定长记录布局与币种约束）
/// </summary>
public enum EftFileFormat
{
    /// <summary>美国 NACHA ACH（94 字符定长记录，本位币 USD）</summary>
    Nacha = 1,

    /// <summary>加拿大 CPA-005（1464 字符逻辑记录，币种 CAD）</summary>
    Cpa005 = 2
}

/// <summary>
/// EFT 批次状态
/// </summary>
public enum EftBatchStatus
{
    /// <summary>草稿（可增删行、可编辑、可作废）</summary>
    Draft = 0,

    /// <summary>已生成（文件已固化加密，不可再改，仅可下载或作废）</summary>
    Generated = 1,

    /// <summary>已作废（行已释放，关联付款可重入其它批次）</summary>
    Voided = 2
}

/// <summary>
/// 收据采集状态
/// </summary>
public enum ReceiptStatus
{
    /// <summary>已上传（尚未提取）</summary>
    Uploaded = 0,

    /// <summary>已提取（字段可人工修正）</summary>
    Extracted = 1,

    /// <summary>已转换（已生成费用/账单草稿，不可删）</summary>
    Converted = 2,

    /// <summary>提取失败（可重试）</summary>
    Failed = 3
}

/// <summary>
/// 由收据转换的目标单据类型
/// </summary>
public enum ReceiptDocType
{
    /// <summary>费用支出（直付）</summary>
    Expense = 1,

    /// <summary>采购账单（形成 AP）</summary>
    Bill = 2
}

/// <summary>
/// 往来方银行账户类型（EFT 交易码派生）
/// </summary>
public enum BankAccountType
{
    /// <summary>支票账户</summary>
    Checking = 1,

    /// <summary>储蓄账户</summary>
    Savings = 2
}

/// <summary>
/// 银行流水来源
/// </summary>
public enum BankTransactionSource
{
    /// <summary>OFX 文件导入</summary>
    Ofx = 1,

    /// <summary>CSV 文件导入</summary>
    Csv = 2,

    /// <summary>银行 feed 提供者拉取</summary>
    Provider = 3
}

/// <summary>
/// 银行流水匹配状态
/// </summary>
public enum BankTransactionStatus
{
    /// <summary>待匹配（未与总账行关联，未被排除）</summary>
    Pending = 0,

    /// <summary>已匹配（生成了对账勾选行）</summary>
    Matched = 1,

    /// <summary>已排除（人工判定为无需入账的噪音行）</summary>
    Excluded = 2
}

/// <summary>
/// 由银行流水创建的单据类型（CreateDocumentAsync 委托既有单据草稿）
/// </summary>
public enum BankFeedDocType
{
    /// <summary>费用支出（付款方向流水，直付）</summary>
    Expense = 1,

    /// <summary>收付款单</summary>
    PaymentEntry = 2,

    /// <summary>资金划转单</summary>
    Transfer = 3
}

/// <summary>
/// 余额汇总校验的差异类型（verify 诊断，不修复）
/// </summary>
public enum BalanceSummaryDifferenceKind
{
    /// <summary>缺失：总账期望有桶，但汇总表无此桶</summary>
    Missing = 1,

    /// <summary>冗余：汇总表有桶，但总账无对应明细</summary>
    Extra = 2,

    /// <summary>不符：桶存在但金额/行数与总账聚合不一致</summary>
    Mismatch = 3
}
