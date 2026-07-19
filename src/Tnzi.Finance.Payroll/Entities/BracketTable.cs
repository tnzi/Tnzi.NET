namespace Tnzi.Finance.Payroll.Entities;

/// <summary>
/// 税级表头（结构内置、内容永不内置：行由 country pack 播种或管理员手录）
/// </summary>
/// <remarks>
/// 同 <see cref="Code"/> 允许多版本（不同 <see cref="EffectiveFrom"/>），
/// 解析规则 = EffectiveFrom ≤ 发薪日 的最大者（见 IBracketTableService.ResolveAsync）。
/// </remarks>
public class BracketTable : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 表编码（必填，统一大写；公式 Bracket(code, amount) 按此定位；
    /// (租户, 编码, 生效日) 唯一）
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 表名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 生效日（date-only；版本键）
    /// </summary>
    public DateTime EffectiveFrom { get; set; }

    /// <summary>
    /// 是否启用（停用的版本不参与解析）
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 税级行（按 Sequence 升序，[LowerBound, UpperBound) 连续不重叠）
    /// </summary>
    public virtual ICollection<BracketRow> Rows { get; set; } = new List<BracketRow>();
}
