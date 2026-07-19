namespace Tnzi.Finance.Payroll.Entities;

/// <summary>
/// 工资单行（单个薪资组件在该工资单上的计算结果 + 组件/公式/科目快照）
/// </summary>
/// <remarks>
/// 行范式（硬删全量重建，随工资单一并物理删除）。组件 Code/Name/Type/公式/科目在计算时
/// 快照落列——过账按行快照聚合到总账（Earning/EmployerContribution → 借费用科目、
/// Deduction/EmployerContribution → 贷负债科目），科目必备性在过账时按 Type 权威校验。
/// </remarks>
public class PayslipLine : EntityBase<Guid>, IMultiTenant
{
    /// <summary>租户</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属工资单</summary>
    public Guid PayslipId { get; set; }

    /// <summary>行序号（沿用结构行序）</summary>
    public int Sequence { get; set; }

    /// <summary>薪资组件</summary>
    public Guid ComponentId { get; set; }

    /// <summary>组件编码快照</summary>
    public string ComponentCode { get; set; } = string.Empty;

    /// <summary>组件名称快照</summary>
    public string ComponentName { get; set; } = string.Empty;

    /// <summary>组件类型快照</summary>
    public SalaryComponentType ComponentType { get; set; }

    /// <summary>计算金额（本位币，已按组件级舍入）</summary>
    public decimal Amount { get; set; }

    /// <summary>该组件的年初至今累计额（含本期，本位币）——合规逐行 YTD 报表所需，
    /// 计算时按 (员工, 组件Code) 从历史已提交批次预取上期 YTD 后加本期额落列快照。</summary>
    public decimal YtdAmount { get; set; }

    /// <summary>生效公式快照（固定额组件为 null）</summary>
    public string? FormulaSnapshot { get; set; }

    /// <summary>费用科目快照（Earning/EmployerContribution 过账借方）</summary>
    public Guid? ExpenseAccountId { get; set; }

    /// <summary>负债科目快照（Deduction/EmployerContribution 过账贷方）</summary>
    public Guid? LiabilityAccountId { get; set; }

    /// <summary>工资单（导航属性）</summary>
    public virtual Payslip? Payslip { get; set; }
}
