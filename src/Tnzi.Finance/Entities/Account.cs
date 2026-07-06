namespace Tnzi.Finance.Entities;

/// <summary>
/// 会计科目（科目表节点）
/// </summary>
/// <remarks>
/// 树形结构：分组科目（<see cref="IsGroup"/> = true）仅用于归类与报表汇总，不可过账；
/// 叶子科目可过账。系统科目通过 <see cref="SystemRole"/> 按角色解析（每租户每角色至多一个），
/// 框架与消费应用不得硬编码科目编码。
/// </remarks>
public class Account : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 科目编码（租户内唯一）
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 科目名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 根类型（会计五要素）
    /// </summary>
    public AccountRootType RootType { get; set; }

    /// <summary>
    /// 子分类（自由词汇，如 Bank / Cash / CreditCard / FixedAsset）
    /// </summary>
    public string? SubType { get; set; }

    /// <summary>
    /// 父科目ID（null 表示根节点）
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// 父科目
    /// </summary>
    public virtual Account? Parent { get; set; }

    /// <summary>
    /// 子科目集合
    /// </summary>
    public virtual ICollection<Account> Children { get; set; } = new List<Account>();

    /// <summary>
    /// 是否分组科目（分组科目不可过账）
    /// </summary>
    public bool IsGroup { get; set; }

    /// <summary>
    /// 科目币种（null 表示不限币种）
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// 系统科目角色（每租户每角色至多一个科目）
    /// </summary>
    public AccountSystemRole? SystemRole { get; set; }

    /// <summary>
    /// 现金流量表活动分类
    /// </summary>
    public CashFlowActivity? CashFlowActivity { get; set; }

    /// <summary>
    /// 是否启用（停用科目不可过账，历史分录保留）
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 该根类型的正常余额方向是否为借方
    /// </summary>
    public static bool IsDebitNormal(AccountRootType rootType)
        => rootType is AccountRootType.Asset or AccountRootType.Expense;
}
