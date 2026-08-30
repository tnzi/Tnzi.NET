namespace Tnzi.Finance.Payroll.Entities;

/// <summary>
/// 一次性输入（本批次 × 本员工 × 本组件的临时金额：奖金、补发、罚扣、预支归还、更正）
/// </summary>
/// <remarks>
/// 公式经 <c>Input()</c> 读取本值 —— 因此它进入的是**按序求值**，
/// 一笔奖金在 GROSS 里，社保与个税据此计算；而不是算完之后追加的一条调整行
/// （那样净额对得上，却错过了应计、应保、应税这三件事）。
/// <para>
/// ★挂在**批次**上而不是工资单上：整批重算（<c>CalculateAsync</c>）会软删旧 payslip 重建，
/// 挂在 payslip 上的输入会随之消失 —— 而"重算一次就丢掉一笔已批准的奖金"是静默错账。
/// 挂在批次上还顺带允许在 Draft 态（首次计算之前）先录入，这正是真实发薪的顺序。
/// </para>
/// <para>
/// ★不用 <c>Employee.AttributesJson</c>：那是员工主数据，不是批次输入。
/// 跑批前写、跑完清是并发不安全的，且会让员工档案长期携带一个只描述某个过去期间的值。
/// </para>
/// </remarks>
public class PayRunInput : MultiTenantAuditedEntity<Guid>
{
    /// <summary>所属发薪批次</summary>
    public Guid PayRunId { get; set; }

    /// <summary>员工</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>薪资组件（其生效公式必须调用 <c>Input()</c>，否则录入端 400）</summary>
    public Guid ComponentId { get; set; }

    /// <summary>金额（本位币；非备注类组件不接受负值，对齐 DefaultAmount/AmountOverride 的口径）</summary>
    public decimal Amount { get; set; }

    /// <summary>事由（安大略 ESA s.12 之类"每一项扣款的金额与用途"的落脚点；组件名给出种类，本字段给出个案）</summary>
    public string? Note { get; set; }
}
