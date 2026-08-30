namespace Tnzi.Finance.Payroll.Services.Internal;

/// <summary>
/// "为这个组件录的一笔一次性金额，跑批时真的会被读到吗" —— 录入端与计算端共用的唯一判据。
/// </summary>
/// <remarks>
/// 一笔录进去却沉默失效的钱，比一个报错危险得多：不报错、不变红、只让实发额少一笔，
/// 要到员工问起来才发现。所以这条判据在**两个时刻**都要问：
/// <list type="bullet">
/// <item>录入那一刻（<c>PayRunService.SetInputAsync</c>）—— 挡住明显录错的目标，直接 400；</item>
/// <item>跑批那一刻（<see cref="PayslipCalculator"/>）—— 录入之后改组件公式、改结构行、
/// 钉上 <c>AmountOverride</c>、把员工换到别的结构，任何一条都能让已录入的金额重新变成孤儿。
/// 入口检查管不到这些，所以出口再核一次，核不过就落 <c>CalculationError</c>（批次因此不可过账）。</item>
/// </list>
/// </remarks>
internal static class PayRunInputBinding
{
    /// <summary>判定某个组件在某条结构行上能否读到它自己的那一笔一次性输入</summary>
    /// <param name="evaluator">公式求值器（只用它的静态函数提取）</param>
    /// <param name="line">该组件在员工本期生效结构里的行；null = 结构里没有这个组件</param>
    /// <param name="component">被录入的薪资组件</param>
    public static PayRunInputBindingStatus Evaluate(
        ISalaryFormulaEvaluator evaluator, SalaryStructureLine? line, SalaryComponent component)
    {
        Check.NotNull(evaluator);
        Check.NotNull(component);

        if (line == null)
            return PayRunInputBindingStatus.NotOnStructure;

        // 行上钉死的金额优先于公式，录进去的输入会被它整个盖掉。
        if (line.AmountOverride.HasValue)
            return PayRunInputBindingStatus.PinnedAmount;

        var formula = string.IsNullOrWhiteSpace(line.FormulaOverride) ? component.Formula : line.FormulaOverride;
        var condition = string.IsNullOrWhiteSpace(line.ConditionOverride) ? component.Condition : line.ConditionOverride;

        return ReadsInput(evaluator, formula) || ReadsInput(evaluator, condition)
            ? PayRunInputBindingStatus.Readable
            : PayRunInputBindingStatus.FormulaIgnoresInput;
    }

    /// <summary>表达式是否调用了 <c>Input()</c>（语法错误的表达式按"读不到"处理，它自有报错通道）</summary>
    private static bool ReadsInput(ISalaryFormulaEvaluator evaluator, string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var functions = evaluator.GetFunctions(expression);
        return functions.Succeeded
            && functions.Data!.Contains(PayrollFormulaFunctions.Input, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>一次性输入能否被读到（<see cref="PayRunInputBinding.Evaluate"/>）</summary>
internal enum PayRunInputBindingStatus
{
    /// <summary>会被读到</summary>
    Readable = 0,

    /// <summary>组件不在该员工本期生效的薪资结构里</summary>
    NotOnStructure = 1,

    /// <summary>结构行钉死了金额，优先于公式</summary>
    PinnedAmount = 2,

    /// <summary>生效公式与条件都不调用 <c>Input()</c></summary>
    FormulaIgnoresInput = 3
}
