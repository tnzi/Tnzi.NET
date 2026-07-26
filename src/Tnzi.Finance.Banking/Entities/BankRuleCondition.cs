namespace Tnzi.Finance.Banking.Entities;

/// <summary>
/// 银行规则的单条条件（随规则全量重建，硬删）
/// </summary>
public class BankRuleCondition : EntityBase<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属规则</summary>
    public Guid BankRuleId { get; set; }

    /// <summary>序号（1 起，仅用于稳定展示顺序）</summary>
    public int LineNumber { get; set; }

    /// <summary>比较字段</summary>
    public BankRuleField Field { get; set; }

    /// <summary>运算符</summary>
    public BankRuleOperator Operator { get; set; }

    /// <summary>比较值（文本字段用原文，金额字段用可解析的数字串）</summary>
    public string Value { get; set; } = string.Empty;
}
