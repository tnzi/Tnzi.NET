namespace Tnzi.Finance.Payroll.Services.Internal;

/// <summary>
/// "这个员工进不进这个批次" —— 计算器与一次性输入录入端共用的唯一判据。
/// </summary>
/// <remarks>
/// 两处必须同口径：<see cref="PayslipCalculator"/> 据此圈选员工，录入端据此拒绝
/// "给一个不在本批次里的人录一笔钱"。判据各写一份的话，漂移的症状恰好是这个功能最怕的
/// 那一个 —— 输入被收下了，跑批时那个人不在批次里，于是一笔已批准的金额安静消失。
/// 所以判据只留一份，两边都来问它。
/// <para>
/// 返回**原因**而不是布尔：圈选侧只关心是不是 <see cref="PayRunEligibilityStatus.Eligible"/>，
/// 录入侧要据此给出说得清的 400。
/// </para>
/// </remarks>
internal static class PayRunEligibility
{
    /// <summary>
    /// 取本期生效的分配：<c>EffectiveFrom ≤ 期末日</c> 的最大者（没有则 null）。
    /// </summary>
    /// <remarks>
    /// 调用方通常已在数据库侧下过同样的 <c>Where</c> 少拉数据，但语义以本方法为准 ——
    /// 预筛是优化，判据在这里。
    /// </remarks>
    public static SalaryAssignment? PickEffective(IEnumerable<SalaryAssignment> assignments, DateTime periodEnd)
        => Check.NotNull(assignments)
            .Where(a => a.EffectiveFrom <= periodEnd)
            .OrderByDescending(a => a.EffectiveFrom)
            .FirstOrDefault();

    /// <summary>
    /// 判定单个员工对某批次的资格。<paramref name="assignment"/> 传
    /// <see cref="PickEffective"/> 的结果。
    /// </summary>
    public static PayRunEligibilityStatus Evaluate(PayRun run, Employee employee, SalaryAssignment? assignment)
    {
        Check.NotNull(run);
        Check.NotNull(employee);

        if (!employee.IsActive)
            return PayRunEligibilityStatus.Inactive;

        // 期初之前就离职的不发；期间内离职的照发（末期工资在这一期）。
        if (employee.TerminationDate.HasValue && employee.TerminationDate.Value.ToUtcDate() < run.PeriodStart.ToUtcDate())
            return PayRunEligibilityStatus.Terminated;

        if (assignment == null)
            return PayRunEligibilityStatus.NoAssignment;

        if (run.StructureId.HasValue && assignment.StructureId != run.StructureId.Value)
            return PayRunEligibilityStatus.StructureMismatch;

        return PayRunEligibilityStatus.Eligible;
    }
}

/// <summary>员工对某批次不合格的原因（<see cref="PayRunEligibility.Evaluate"/>）</summary>
internal enum PayRunEligibilityStatus
{
    /// <summary>进本批次</summary>
    Eligible = 0,

    /// <summary>员工已停用</summary>
    Inactive = 1,

    /// <summary>期初之前已离职</summary>
    Terminated = 2,

    /// <summary>本期没有生效的薪资分配</summary>
    NoAssignment = 3,

    /// <summary>批次按薪资结构过滤，而该员工的分配指向另一个结构</summary>
    StructureMismatch = 4
}
