namespace Tnzi.Finance.Banking.Entities;

/// <summary>
/// 银行规则：把一条对账单流水自动认成某种单据
/// </summary>
/// <remarks>
/// 匹配引擎回答的是"这笔流水对应账上哪一行"，规则回答的是**另一半问题**——
/// "账上根本没有这一行，但我知道它是什么"（星巴克 → 餐饮费）。前者是对账，
/// 后者是记账，两者互补而非替代：规则只在匹配引擎找不到对手方时才参与。
///
/// **首个命中者胜**（QuickBooks 语义）：按 <see cref="Priority"/> 升序取第一条
/// 命中的规则。不做"多条规则合并结果"——两条规则给出不同科目时，合并的结果是
/// 谁也说不清钱去了哪儿，而顺序至少是操作员能看见、能调整的。
///
/// <see cref="AccountId"/> 为 null = 适用于全部银行账户；指定则只作用于该账户
/// （不同账户的用途往往不同，同一段摘要在两个账户上未必是同一件事）。
/// </remarks>
public class BankRule : MultiTenantAuditedEntity<Guid>
{
    /// <summary>规则名（操作员可读，出现在流水行上说明"为什么这样归类"）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>优先级（升序；首个命中者胜）</summary>
    public int Priority { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>限定银行账户（null = 全部账户）</summary>
    public Guid? AccountId { get; set; }

    /// <summary>限定资金方向</summary>
    public BankRuleDirection Direction { get; set; } = BankRuleDirection.Any;

    /// <summary>条件之间的关系</summary>
    public BankRuleMatchMode MatchMode { get; set; } = BankRuleMatchMode.All;

    // ── 动作 ────────────────────────────────────────────────

    /// <summary>建议创建的单据类型</summary>
    public BankFeedDocType DocType { get; set; } = BankFeedDocType.Expense;

    /// <summary>对方科目（Expense 的费用科目 / Transfer 的另一侧资金科目）</summary>
    public Guid? CounterAccountId { get; set; }

    /// <summary>往来方（PaymentEntry 必填）</summary>
    public Guid? PartyId { get; set; }

    /// <summary>结算方式</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>
    /// 命中即自动建单、过账并确认匹配，不等人点。
    /// </summary>
    /// <remarks>
    /// 逐规则开关而不是全局开关：值得自动入账的是"每月同一笔房租"这种确定的
    /// 流水，而不是"所有含 AMAZON 的支出"。默认关闭——自动写进总账的东西，
    /// 必须是操作员逐条想清楚后主动打开的。
    /// </remarks>
    public bool AutoApply { get; set; }

    /// <summary>条件</summary>
    public virtual ICollection<BankRuleCondition> Conditions { get; set; } = new List<BankRuleCondition>();
}
