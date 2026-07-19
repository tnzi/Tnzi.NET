namespace Tnzi.Finance.Payroll.Entities;

/// <summary>
/// 税级行（随表头整体重建，无软删除）
/// </summary>
/// <remarks>
/// 区间语义 [LowerBound, UpperBound)：命中行含下界不含上界。
/// 有 <see cref="QuickDeduction"/> 的行按速算扣除数求税（amount × Rate − QuickDeduction），
/// 无则逐级累进求和（见 BracketMath）。
/// </remarks>
public class BracketRow : EntityBase<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属税级表</summary>
    public Guid TableId { get; set; }

    /// <summary>行序号（升序）</summary>
    public int Sequence { get; set; }

    /// <summary>区间下界（含；首行必须为 0）</summary>
    public decimal LowerBound { get; set; }

    /// <summary>区间上界（不含；null 表示 +∞，仅末行允许）</summary>
    public decimal? UpperBound { get; set; }

    /// <summary>税率（小数形式，如 0.10 表示 10%）</summary>
    public decimal Rate { get; set; }

    /// <summary>速算扣除数（null 表示该表按逐级累进求和）</summary>
    public decimal? QuickDeduction { get; set; }
}
