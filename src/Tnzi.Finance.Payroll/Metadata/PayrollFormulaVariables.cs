namespace Tnzi.Finance.Payroll.Metadata;

/// <summary>
/// 薪资公式内置变量名。
/// 结构行公式的允许变量集 = 本集合 ∪ 更早序号行的组件 Code。
/// </summary>
public static class PayrollFormulaVariables
{
    /// <summary>分配基薪（SalaryAssignment.BaseAmount）</summary>
    public const string Base = "BASE";

    /// <summary>按序已计算的 Earning 组件累计（毛收入滚动值）</summary>
    public const string Gross = "GROSS";

    /// <summary>期间实际出勤天数（手动输入）</summary>
    public const string WorkedDays = "WORKED_DAYS";

    /// <summary>期间总天数</summary>
    public const string PeriodDays = "PERIOD_DAYS";

    /// <summary>每年发薪期数（12/24/26/52，由结构频率推导）</summary>
    public const string PeriodsPerYear = "PERIODS_PER_YEAR";

    /// <summary>全部内置变量（结构校验的基础允许集）</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Base, Gross, WorkedDays, PeriodDays, PeriodsPerYear
    };
}

/// <summary>
/// <c>Ytd(code)</c> 的**聚合**参数：按组件类型汇总本年度已提交批次的累计，
/// 而不是某一个组件编码的累计。
///
/// ★ 存在的理由：一部分法定上限压在**累计毛收入**上（加拿大 CPP2 的第二封顶、
/// 美国社保的 wage base），不压在某一个组件上。只能按编码取的话，公式必须把当年
/// 所有收入项逐个列全并相加——明年新增一个收入项、忘了加进这个和，上限就会静默地
/// 少扣，而且要到年终对账才看得出来。这正是把上限做成表所要消灭的那种失效模式。
///
/// 编码以 <c>#</c> 开头，而组件 Code 的正则是 <c>^[A-Z][A-Z0-9_]*$</c>，
/// 所以这些名字**在构造上**不可能与任何组件撞名。
/// </summary>
public static class PayrollYtdAggregates
{
    /// <summary>本年度已提交批次的收入项（Earning）累计</summary>
    public const string Gross = "#GROSS";

    /// <summary>本年度已提交批次的扣减项（Deduction）累计</summary>
    public const string Deductions = "#DEDUCTIONS";

    /// <summary>本年度已提交批次的雇主承担项（EmployerContribution）累计</summary>
    public const string EmployerCost = "#EMPLOYER";

    /// <summary>本年度已提交批次的实发额累计（收入 − 扣减）</summary>
    public const string Net = "#NET";

    /// <summary>全部聚合键</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Gross, Deductions, EmployerCost, Net
    };
}
