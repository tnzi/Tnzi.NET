namespace Tnzi.Finance.Banking.Dtos;

/// <summary>
/// 银行规则 DTO
/// </summary>
public class BankRuleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool IsEnabled { get; set; }
    public Guid? AccountId { get; set; }
    public string? AccountName { get; set; }
    public BankRuleDirection Direction { get; set; }
    public BankRuleMatchMode MatchMode { get; set; }
    public BankFeedDocType DocType { get; set; }
    public Guid? CounterAccountId { get; set; }
    public string? CounterAccountName { get; set; }
    public Guid? PartyId { get; set; }
    public string? PaymentMethod { get; set; }
    public bool AutoApply { get; set; }
    public DateTime CreationTime { get; set; }
    public List<BankRuleConditionDto> Conditions { get; set; } = new();
}

/// <summary>
/// 银行规则条件 DTO
/// </summary>
public class BankRuleConditionDto
{
    public Guid Id { get; set; }
    public int LineNumber { get; set; }
    public BankRuleField Field { get; set; }
    public BankRuleOperator Operator { get; set; }
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// 创建/更新银行规则请求
/// </summary>
public class CreateBankRuleDto
{
    public string Name { get; set; } = string.Empty;

    /// <summary>优先级（未指定时排到末尾）</summary>
    public int? Priority { get; set; }

    public bool IsEnabled { get; set; } = true;
    public Guid? AccountId { get; set; }
    public BankRuleDirection Direction { get; set; } = BankRuleDirection.Any;
    public BankRuleMatchMode MatchMode { get; set; } = BankRuleMatchMode.All;
    public BankFeedDocType DocType { get; set; } = BankFeedDocType.Expense;
    public Guid? CounterAccountId { get; set; }
    public Guid? PartyId { get; set; }
    public string? PaymentMethod { get; set; }
    public bool AutoApply { get; set; }
    public List<CreateBankRuleConditionDto> Conditions { get; set; } = null!;
}

/// <summary>
/// 银行规则条件请求
/// </summary>
public class CreateBankRuleConditionDto
{
    public BankRuleField Field { get; set; }
    public BankRuleOperator Operator { get; set; }
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// 规则排序请求（全量提交本次可见的顺序）
/// </summary>
public class ReorderBankRulesDto
{
    /// <summary>按新顺序排列的规则 Id</summary>
    public List<Guid> RuleIds { get; set; } = null!;
}

/// <summary>
/// 规则试跑请求
/// </summary>
/// <remarks>
/// 「首个命中者胜」意味着新加一条规则可能悄悄抢走旧规则的流水。试跑让操作员在
/// 保存之前就看见"这条规则会命中哪些还没入账的流水"，而不是等它自动记完账才发现。
/// </remarks>
public class TestBankRuleDto
{
    /// <summary>限定账户（null = 全部）</summary>
    public Guid? AccountId { get; set; }

    /// <summary>最多返回多少条命中样本</summary>
    public int Sample { get; set; } = 20;
}

/// <summary>
/// 规则试跑结果
/// </summary>
public class BankRuleTestResultDto
{
    /// <summary>评估的待匹配流水数</summary>
    public int Evaluated { get; set; }

    /// <summary>命中数</summary>
    public int Matched { get; set; }

    /// <summary>命中样本</summary>
    public List<BankRuleTestRowDto> Rows { get; set; } = new();
}

/// <summary>
/// 试跑命中的一条流水
/// </summary>
public class BankRuleTestRowDto
{
    public Guid TransactionId { get; set; }
    public DateTime TxnDate { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public string? Payee { get; set; }

    /// <summary>
    /// 这条流水实际会被哪条规则拿走。
    /// </summary>
    /// <remarks>
    /// 与被试跑的规则不同时，说明有更高优先级的规则抢在前面——这正是操作员需要
    /// 在保存前看见的事实。
    /// </remarks>
    public Guid WinningRuleId { get; set; }

    public string WinningRuleName { get; set; } = string.Empty;
}

/// <summary>
/// 银行规则查询
/// </summary>
public class BankRuleQueryDto : PagedQueryDto
{
    /// <summary>关键字（规则名）</summary>
    public string? Keyword { get; set; }

    /// <summary>按账户过滤（含"全部账户"规则）</summary>
    public Guid? AccountId { get; set; }

    public bool? IsEnabled { get; set; }
}
