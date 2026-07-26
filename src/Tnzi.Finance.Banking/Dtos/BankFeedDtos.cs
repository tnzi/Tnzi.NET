namespace Tnzi.Finance.Banking.Dtos;

/// <summary>
/// 银行流水行（响应）
/// </summary>
public class BankTransactionDto
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid ImportBatchId { get; set; }
    public DateTime TxnDate { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Payee { get; set; }
    public string? Reference { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public BankTransactionSource Source { get; set; }
    public BankTransactionStatus Status { get; set; }
    public Guid? MatchedJournalLineId { get; set; }
    public Guid? ReconciliationLineId { get; set; }
    public Guid? SuggestedJournalLineId { get; set; }
    public decimal? MatchConfidence { get; set; }
    public string? MatchRule { get; set; }

    /// <summary>命中的银行规则</summary>
    public Guid? SuggestedRuleId { get; set; }

    /// <summary>规则名（界面据此说明"为什么这样归类"）</summary>
    public string? SuggestedRuleName { get; set; }

    /// <summary>规则建议创建的单据类型</summary>
    public BankFeedDocType? SuggestedDocType { get; set; }

    /// <summary>规则建议的归类科目</summary>
    public Guid? SuggestedCounterAccountId { get; set; }

    /// <summary>归类科目名（省去呈现端逐行再查一次）</summary>
    public string? SuggestedCounterAccountName { get; set; }

    /// <summary>规则建议的往来方</summary>
    public Guid? SuggestedPartyId { get; set; }

    /// <summary>规则建议的结算方式</summary>
    public string? SuggestedPaymentMethod { get; set; }

    public decimal? BalanceAfter { get; set; }
    public string? CreatedDocType { get; set; }
    public Guid? CreatedDocId { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 银行流水查询
/// </summary>
public class BankTransactionQueryDto : PagedQueryDto
{
    public Guid? AccountId { get; set; }
    public Guid? ImportBatchId { get; set; }
    public BankTransactionStatus? Status { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? Keyword { get; set; }
}

/// <summary>
/// CSV 列映射（随导入请求传递，不持久化；前端按账户 localStorage 记忆）
/// </summary>
/// <remarks>列索引 0 基；AmountColumn 与 (DebitColumn + CreditColumn) 二选一。</remarks>
public class CsvMappingDto
{
    /// <summary>是否含表头行</summary>
    public bool HasHeader { get; set; } = true;

    /// <summary>分隔符（默认 ","）</summary>
    public string Delimiter { get; set; } = ",";

    /// <summary>日期列索引</summary>
    public int DateColumn { get; set; }

    /// <summary>日期格式（如 "yyyy-MM-dd"；留空回退宽松解析）</summary>
    public string? DateFormat { get; set; }

    /// <summary>金额列索引（带符号单列；与借/贷双列二选一）</summary>
    public int? AmountColumn { get; set; }

    /// <summary>出款（借方/withdrawal）列索引</summary>
    public int? DebitColumn { get; set; }

    /// <summary>入款（贷方/deposit）列索引</summary>
    public int? CreditColumn { get; set; }

    /// <summary>摘要列索引</summary>
    public int? DescriptionColumn { get; set; }

    /// <summary>参考号列索引</summary>
    public int? ReferenceColumn { get; set; }

    /// <summary>数据行前跳过的行数（不含表头）</summary>
    public int SkipRows { get; set; }

    /// <summary>小数分隔符（"," = 欧洲格式，先去千分位再归一化）</summary>
    public string? DecimalSeparator { get; set; }

    /// <summary>流水币种（留空回退银行账户档案币种/本位币）</summary>
    public string? Currency { get; set; }
}

/// <summary>
/// 导入统计结果
/// </summary>
public class BankImportResultDto
{
    public Guid BatchId { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
}

/// <summary>
/// 从银行 feed 提供者拉取
/// </summary>
public class PullBankFeedDto
{
    public Guid AccountId { get; set; }
}

/// <summary>
/// 匹配建议运行结果
/// </summary>
public class BankSuggestResultDto
{
    /// <summary>评估的待匹配行数</summary>
    public int Evaluated { get; set; }

    /// <summary>产生建议的行数</summary>
    public int Suggested { get; set; }

    /// <summary>自动确认的行数（开启 auto-confirm 且存在 Draft 对账）</summary>
    public int AutoConfirmed { get; set; }

    /// <summary>由银行规则给出建议的行数（账上无对手方，规则说它是什么）</summary>
    public int RuleSuggested { get; set; }

    /// <summary>由 AutoApply 规则自动建单+过账+确认匹配的行数</summary>
    public int AutoCategorized { get; set; }
}

/// <summary>
/// 确认匹配（journalLineId 留空则采用引擎建议）
/// </summary>
public class ConfirmBankMatchDto
{
    public Guid? JournalLineId { get; set; }
}

/// <summary>
/// 匹配候选（供用户在多候选时挑选）
/// </summary>
public class BankMatchCandidateDto
{
    public Guid JournalLineId { get; set; }
    public Guid JournalEntryId { get; set; }
    public string? EntryNumber { get; set; }
    public DateTime PostingDate { get; set; }
    public string? Memo { get; set; }

    /// <summary>行净额（借方为正，本位币；与流水金额同号比较）</summary>
    public decimal Amount { get; set; }
}

/// <summary>
/// 由银行流水创建单据草稿（按符号预填，委托既有 CreateDraftAsync）
/// </summary>
public class CreateBankDocumentDto
{
    public BankFeedDocType DocType { get; set; }

    /// <summary>对方科目（Expense 的费用科目 / Transfer 的另一侧资金科目）</summary>
    public Guid? CounterAccountId { get; set; }

    /// <summary>往来方（PaymentEntry 必填）</summary>
    public Guid? PartyId { get; set; }

    /// <summary>结算方式</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>
    /// 一步完成：建草稿 → 过账 → 用过账产生的分录行确认匹配（默认 false，仅建草稿）。
    /// </summary>
    /// <remarks>
    /// 对账界面的「新建并对账」正是这一步：只建草稿会把操作员踢出对账节奏
    /// （去单据页过账 → 回来 → 重跑建议 → 再确认，四次跳转），而对账流程的
    /// 全部价值就在于节奏。<br/>
    /// 前置条件与 <c>ConfirmMatchAsync</c> 完全一致（本位币科目 + 该科目存在
    /// Draft 对账），且**在建任何东西之前**校验：拒绝路径零写入，不会留下一张
    /// 无人认领的草稿单据。
    /// </remarks>
    public bool PostAndMatch { get; set; }
}

/// <summary>
/// 创建单据的结果
/// </summary>
public class BankDocumentResultDto
{
    public string DocType { get; set; } = string.Empty;
    public Guid DocId { get; set; }

    /// <summary>单据是否已过账（<see cref="CreateBankDocumentDto.PostAndMatch"/> 为真时）</summary>
    public bool Posted { get; set; }

    /// <summary>流水是否已确认匹配（过账后自动确认）</summary>
    public bool Matched { get; set; }

    /// <summary>过账产生的凭证（未过账时为 null）</summary>
    public Guid? JournalEntryId { get; set; }
}

/// <summary>
/// 银行流水导入批次（响应）
/// </summary>
public class BankImportBatchDto
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string? AccountName { get; set; }
    public BankTransactionSource Source { get; set; }
    public string? FileName { get; set; }
    public DateTime? PeriodFrom { get; set; }
    public DateTime? PeriodTo { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public decimal? StatementEndBalance { get; set; }

    /// <summary>批内已匹配行数（>0 时不可删除）</summary>
    public int MatchedCount { get; set; }

    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 银行流水导入批次查询
/// </summary>
public class BankImportBatchQueryDto : PagedQueryDto
{
    public Guid? AccountId { get; set; }
}
