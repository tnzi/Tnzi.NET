namespace Tnzi.Finance.Payroll.Entities;

/// <summary>
/// 薪资组件（收入/扣减/雇主承担项的目录定义；<see cref="Code"/> 即公式变量名）
/// </summary>
/// <remarks>
/// 科目字段按 <see cref="Metadata.SalaryComponentType"/> 的必备性在过账时权威校验
/// （Earning 须有费用科目、Deduction 须有负债科目、EmployerContribution 双边都须有），
/// 保存期不强制，允许先建目录后补科目。
/// </remarks>
public class SalaryComponent : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 组件编码（必填，租户内唯一；即公式变量名，须匹配 ^[A-Z][A-Z0-9_]*$）
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 组件名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 组件类型
    /// </summary>
    public SalaryComponentType Type { get; set; }

    /// <summary>
    /// 金额公式（null 表示固定额，取 <see cref="DefaultAmount"/> 或结构行覆盖额）
    /// </summary>
    public string? Formula { get; set; }

    /// <summary>
    /// 适用条件（布尔表达式；null 表示恒真）
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>
    /// 默认固定金额（无公式时使用；结构行 AmountOverride 优先）
    /// </summary>
    public decimal? DefaultAmount { get; set; }

    /// <summary>
    /// 费用科目（Earning/EmployerContribution 过账借方）
    /// </summary>
    public Guid? ExpenseAccountId { get; set; }

    /// <summary>
    /// 负债科目（Deduction/EmployerContribution 过账贷方）
    /// </summary>
    public Guid? LiabilityAccountId { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }
}
