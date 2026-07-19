namespace Tnzi.Finance.Payroll.Entities;

/// <summary>
/// 薪资结构行（结构 → 组件的有序引用；随结构整体重建，无软删除）
/// </summary>
/// <remarks>
/// 行级覆盖优先于组件目录定义：生效公式 = FormulaOverride ?? 组件.Formula，
/// 生效条件 = ConditionOverride ?? 组件.Condition，
/// 固定额 = AmountOverride ?? 组件.DefaultAmount。
/// </remarks>
public class SalaryStructureLine : EntityBase<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属结构</summary>
    public Guid StructureId { get; set; }

    /// <summary>引用组件</summary>
    public Guid ComponentId { get; set; }

    /// <summary>求值序号（升序；公式只能引用更早序号行的组件 Code）</summary>
    public int Sequence { get; set; }

    /// <summary>公式覆盖（null 沿用组件公式）</summary>
    public string? FormulaOverride { get; set; }

    /// <summary>固定额覆盖（null 沿用组件默认额）</summary>
    public decimal? AmountOverride { get; set; }

    /// <summary>条件覆盖（null 沿用组件条件）</summary>
    public string? ConditionOverride { get; set; }
}
