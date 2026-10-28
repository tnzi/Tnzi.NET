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
    /// <see cref="PickEffective"/> 的结果；<paramref name="structureFrequency"/> 传该分配所指
    /// 薪资结构的发薪频率（结构已不存在时传 null，不在这里排除，让计算端把它报成错误工资单）。
    /// </summary>
    public static PayRunEligibilityStatus Evaluate(PayRun run, Employee employee, SalaryAssignment? assignment, PayFrequency? structureFrequency)
    {
        Check.NotNull(run);
        Check.NotNull(employee);

        if (!employee.IsActive)
            return PayRunEligibilityStatus.Inactive;

        // 期初之前就离职的不发；期间内离职的照发（末期工资在这一期）。
        if (employee.TerminationDate.HasValue && employee.TerminationDate.Value.ToUtcDate() < run.PeriodStart.ToUtcDate())
            return PayRunEligibilityStatus.Terminated;

        // 与离职日对称：期末之后才入职的不发；期间内入职的照发（首期工资在这一期，出勤天数由人填）。
        // 分配生效日是另一回事，管理端新建分配默认生效日为当天，提前录入的新人因此会带着
        // 一条「已生效」的分配进入入职之前的那一期，并按整期计算。
        if (employee.HireDate.HasValue && employee.HireDate.Value.ToUtcDate() > run.PeriodEnd.ToUtcDate())
            return PayRunEligibilityStatus.NotYetHired;

        if (assignment == null)
            return PayRunEligibilityStatus.NoAssignment;

        if (run.StructureId.HasValue && assignment.StructureId != run.StructureId.Value)
            return PayRunEligibilityStatus.StructureMismatch;

        // ★ 批次只有一个 PERIODS_PER_YEAR（取自 run.Frequency）。不选结构过滤的月薪批次若把周薪
        // 结构上的员工也圈进来，就会按 12 期/年给他们算一整月，而他们的周薪批次还会再发一遍，
        // 没有任何报错。所以频率不同的员工不进这个批次。
        if (structureFrequency.HasValue && structureFrequency.Value != run.Frequency)
            return PayRunEligibilityStatus.FrequencyMismatch;

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
    StructureMismatch = 4,

    /// <summary>员工的入职日晚于本期期末</summary>
    NotYetHired = 5,

    /// <summary>该员工的薪资结构发薪频率与批次不同</summary>
    FrequencyMismatch = 6
}
