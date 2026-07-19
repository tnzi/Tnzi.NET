namespace Tnzi.Finance.Payroll.Entities;

/// <summary>
/// 薪资分配（员工 → 结构 + 基薪，按生效日版本化）
/// </summary>
/// <remarks>
/// 无 EffectiveTo：后一条分配自然截断前一条（Frappe 模式），
/// 解析规则 = EffectiveFrom ≤ 期末日 的最大者。
/// (租户, 员工, 生效日) 唯一——同日只允许一条，修正 = 删除重建。
/// </remarks>
public class SalaryAssignment : MultiTenantAuditedEntity<Guid>
{
    /// <summary>员工</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>薪资结构</summary>
    public Guid StructureId { get; set; }

    /// <summary>生效日（date-only）</summary>
    public DateTime EffectiveFrom { get; set; }

    /// <summary>基薪（本位币；公式变量 BASE）</summary>
    public decimal BaseAmount { get; set; }

    /// <summary>备注</summary>
    public string? Notes { get; set; }
}
