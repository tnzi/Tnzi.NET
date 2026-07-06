namespace Tnzi.Finance.Options;

/// <summary>
/// 财务模块配置
/// </summary>
/// <remarks>
/// BaseCurrency 是账本的本位币：所有总账行同时存储交易币金额与本位币金额，
/// 报表按本位币聚合。**账本产生分录后不得更改本位币**（历史分录不会重算），
/// 因此该项为纯启动配置，不提供运行时热设置。
/// </remarks>
[ConfigSection("Finance")]
public class FinanceOptions
{
    /// <summary>
    /// 本位币（ISO 4217 货币代码，默认 USD）
    /// </summary>
    public string BaseCurrency { get; set; } = "USD";

    /// <summary>
    /// 本位币金额舍入小数位（多币种换算后按此位数舍入，默认 2）
    /// </summary>
    public int BaseCurrencyDecimals { get; set; } = 2;

    /// <summary>
    /// 过账配平容差（本位币金额绝对值）。
    /// 多币种换算后借贷尾差在容差内时，自动生成舍入差额行
    /// （记入 <see cref="Metadata.AccountSystemRole.RoundingDifference"/> 角色科目）；
    /// 超出容差则拒绝过账
    /// </summary>
    public decimal RoundingTolerance { get; set; } = 0.05m;

    /// <summary>
    /// 凭证编号前缀
    /// </summary>
    public string JournalNumberPrefix { get; set; } = "JE-";

    /// <summary>
    /// 凭证编号数字部分补零位数（0 表示不补零）
    /// </summary>
    public int JournalNumberPadding { get; set; } = 6;

    /// <summary>
    /// 过账是否要求过账日期落在已定义且未关闭的会计年度内。
    /// false（默认）：未定义会计年度时允许任意日期过账（零配置可用），
    /// 已定义时仅校验"不得落入已关闭年度"；
    /// true：过账日期必须落在某个未关闭的会计年度内
    /// </summary>
    public bool RequireFiscalYearForPosting { get; set; }

    /// <summary>
    /// 单张凭证最大分录行数（防御性上限）
    /// </summary>
    public int MaxLinesPerEntry { get; set; } = 500;

    /// <summary>
    /// 默认付款账期天数（客户/供应商未单独配置时使用，P2 单据据此推 DueDate）
    /// </summary>
    public int DefaultPaymentTermsDays { get; set; } = 30;

    /// <summary>发票编号前缀（数字部分补零位数沿用 <see cref="JournalNumberPadding"/>）</summary>
    public string InvoiceNumberPrefix { get; set; } = "INV-";

    /// <summary>账单编号前缀</summary>
    public string BillNumberPrefix { get; set; } = "BILL-";

    /// <summary>费用支出编号前缀</summary>
    public string ExpenseNumberPrefix { get; set; } = "EXP-";

    /// <summary>贷项单编号前缀</summary>
    public string CreditMemoNumberPrefix { get; set; } = "CM-";

    /// <summary>收付款单编号前缀</summary>
    public string PaymentNumberPrefix { get; set; } = "PMT-";

    /// <summary>
    /// 收款（Inbound）未指定存入科目时是否过账到待存款项（UndepositedFunds 角色科目）。
    /// false（默认）：必须显式指定存入科目
    /// </summary>
    public bool PostToUndepositedFunds { get; set; }
}
