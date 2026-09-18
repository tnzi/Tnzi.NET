namespace Tnzi.Finance.Payroll.Services.Internal;

/// <summary>
/// 组件金额的符号规则 —— 三条写入路径（计算器 / 一次性输入 / 外部摄取）共用的唯一判据。
/// </summary>
/// <remarks>
/// <para>
/// 收入、扣减、雇主承担项是要进合计、进分录、印在工资条上的钱，必须非负：
/// 一个负的扣减项就是一次没人申报的加薪（Deduction = -500 让实发多 500），而它能骗过每一道只看合计的守卫 ——
/// 净额仍为正、批次无 <c>CalculationError</c>、过账引擎按科目聚合后仍为正 —— 于是干净地过账，
/// 并被 <c>Ytd()</c> 折进此后每一期的法定上限基数。
/// </para>
/// <para>
/// <see cref="SalaryComponentType.Informational"/> 是具名中间量不是钱，天然带符号（抵免、冲回），
/// 且进不了任何合计，所以造不出荒谬的净额 —— 它是唯一允许为负的类型。
/// </para>
/// <para>
/// ★ 抽成一处的理由：外部摄取曾漏掉这条规则（2026-09-12），而另外两条路径各自抄了一遍。
/// 三处各写一份，下一个新写入路径照样会漏。
/// </para>
/// </remarks>
internal static class PayrollAmountRules
{
    /// <summary>这笔金额对该类型的组件来说是不是一个被禁止的负数。</summary>
    internal static bool IsNegativeMonetary(SalaryComponentType type, decimal amount)
        => amount < 0 && type != SalaryComponentType.Informational;
}
