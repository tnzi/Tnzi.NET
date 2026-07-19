namespace Tnzi.Finance.Payroll.Entities;

/// <summary>
/// 薪资结构（组件的有序组合；发薪批次按结构行序求值）
/// </summary>
/// <remarks>
/// 结构不做日期版本化：历史保真由 payslip 快照承担（P4c 落 PayslipLine 的公式与输入快照）。
/// </remarks>
public class SalaryStructure : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 结构名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 发薪频率
    /// </summary>
    public PayFrequency Frequency { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 结构行（按 Sequence 升序求值；保存时全量重建）
    /// </summary>
    public virtual ICollection<SalaryStructureLine> Lines { get; set; } = new List<SalaryStructureLine>();
}
