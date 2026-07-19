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
