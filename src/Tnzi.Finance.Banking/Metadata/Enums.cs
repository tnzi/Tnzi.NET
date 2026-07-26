namespace Tnzi.Finance.Banking.Metadata;

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
/// 银行规则的条件字段
/// </summary>
/// <remarks>
/// 刻意只覆盖对账单本身携带的字段。更花哨的判据（按往来方历史、按 MCC、按机器
/// 学习）不属于框架该猜的范围——消费应用替换 <c>IBankRuleEvaluator</c> 即可，
/// 那才是留给它们的扩展点。
/// </remarks>
public enum BankRuleField
{
    /// <summary>摘要</summary>
    Description = 1,

    /// <summary>收/付款方</summary>
    Payee = 2,

    /// <summary>参考号</summary>
    Reference = 3,

    /// <summary>金额（比较用绝对值，方向另由 <see cref="BankRuleDirection"/> 表达）</summary>
    Amount = 4
}

/// <summary>
/// 银行规则的条件运算符
/// </summary>
public enum BankRuleOperator
{
    Contains = 1,
    NotContains = 2,
    Equals = 3,
    StartsWith = 4,
    EndsWith = 5,

    /// <summary>大于（仅金额字段）</summary>
    GreaterThan = 6,

    /// <summary>小于（仅金额字段）</summary>
    LessThan = 7
}

/// <summary>
/// 多个条件之间的关系
/// </summary>
public enum BankRuleMatchMode
{
    /// <summary>全部满足</summary>
    All = 1,

    /// <summary>满足任意一个</summary>
    Any = 2
}

/// <summary>
/// 规则适用的资金方向
/// </summary>
/// <remarks>
/// 方向单列而不是塞进金额条件：一条"星巴克 → 餐饮费"的规则只该匹配支出，若某天
/// 银行退了一笔款进来，它不该被自动记成一笔餐饮费。
/// </remarks>
public enum BankRuleDirection
{
    /// <summary>不限</summary>
    Any = 0,

    /// <summary>存入（流水金额为正）</summary>
    MoneyIn = 1,

    /// <summary>支出（流水金额为负）</summary>
    MoneyOut = 2
}
