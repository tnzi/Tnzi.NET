namespace Tnzi.Finance.Payroll.Services.Internal;

/// <summary>
/// 薪资结构行的静态规则：结构保存、组件更新与计算端共用的唯一一份判据。
/// </summary>
/// <remarks>
/// 一条结构行的生效公式/条件由「行覆盖 ∪ 组件定义」合成，所以结构合不合规不只取决于结构本身：
/// 改组件的公式、条件、类型或编码，同样能让一个保存时合规的结构失效。两处各写一份的话，
/// 漂移的症状正是这些规则要防的那一种：静默少税、负的扣减项、引用一个此刻还没算出来的值。
/// </remarks>
internal static class SalaryStructureRules
{
    /// <summary>结构行的规则视图（入参 DTO 与已落库的结构行两种来源）</summary>
    internal readonly record struct LineSpec(
        int Sequence, Guid ComponentId, string? FormulaOverride, string? ConditionOverride, decimal? AmountOverride);

    internal static LineSpec ToSpec(SalaryStructureLine line)
        => new(line.Sequence, line.ComponentId, line.FormulaOverride, line.ConditionOverride, line.AmountOverride);

    internal static LineSpec ToSpec(SalaryStructureLineInputDto line)
        => new(line.Sequence, line.ComponentId, line.FormulaOverride, line.ConditionOverride, line.AmountOverride);

    /// <summary>
    /// 逐行校验（按序号）：组件存在（且按需启用）、钉死额符号、公式/条件只引用允许集、读 GROSS 的行排在最后一个收入行之后。
    /// </summary>
    /// <param name="lines">结构行（序号与组件不重复由调用方先行保证）</param>
    /// <param name="components">行所引用的组件（按 Id）</param>
    /// <param name="externalVariables">计算钩子声明会注入的变量名</param>
    /// <param name="evaluator">公式求值器（只用它的静态变量提取）</param>
    /// <param name="requireActiveComponents">停用的组件是否算违规（结构保存时是；组件更新回查时不是，停用另有其义）</param>
    public static Result ValidateLines(
        IReadOnlyCollection<LineSpec> lines,
        IReadOnlyDictionary<Guid, SalaryComponent> components,
        IReadOnlySet<string> externalVariables,
        ISalaryFormulaEvaluator evaluator,
        bool requireActiveComponents)
    {
        Check.NotNull(lines);
        Check.NotNull(components);
        Check.NotNull(externalVariables);
        Check.NotNull(evaluator);

        var orderedLines = lines.OrderBy(l => l.Sequence).ToList();
        var lastEarningSequence = LastEarningSequence(orderedLines, components);

        // 允许集从内置变量起步，随序号推进滚入已算组件的 Code；
        // 再并进计算钩子**声明**会注入的外部变量（法定标量随辖区/生效日变化，值在跑批时现算，
        // 但名字静态可知）。不并进来的话，运行期注入得再对也没有公式敢引用它。
        var allowed = new HashSet<string>(PayrollFormulaVariables.All, StringComparer.Ordinal);
        allowed.UnionWith(externalVariables);

        foreach (var line in orderedLines)
        {
            if (!components.TryGetValue(line.ComponentId, out var component))
                return Result.Failure($"Salary component '{line.ComponentId}' not found.", 404);
            if (requireActiveComponents && !component.IsActive)
                return Result.Failure($"Salary component '{component.Code}' is inactive and cannot be added to a structure.", 400);
            if (externalVariables.Contains(component.Code))
                return Result.Failure(DescribeExternalVariableCollision(line.Sequence, component.Code), 400);
            // 钉死额的符号规则与 DefaultAmount / 一次性输入同一份判据：备注项是具名中间量，天然带符号。
            if (line.AmountOverride is { } amountOverride && PayrollAmountRules.IsNegativeMonetary(component.Type, amountOverride))
                return Result.Failure($"AmountOverride cannot be negative for component '{component.Code}' ({component.Type}).", 400);

            var (formula, condition) = EffectiveExpressions(line, component);

            var formulaCheck = ValidateExpression(evaluator, formula, allowed, component.Code, "formula");
            if (!formulaCheck.Succeeded)
                return formulaCheck;

            var conditionCheck = ValidateExpression(evaluator, condition, allowed, component.Code, "condition");
            if (!conditionCheck.Succeeded)
                return conditionCheck;

            if (ReadsGrossTooEarly(evaluator, line, formula, condition, lastEarningSequence))
                return Result.Failure(DescribeGrossReadTooEarly(line.Sequence, component.Code), 400);

            allowed.Add(component.Code);
        }

        return Result.Success();
    }

    /// <summary>
    /// 第一条在最后一个收入行之前读 GROSS 的行（有则给出错误文案，null = 没有）。
    /// </summary>
    /// <remarks>
    /// GROSS 是按序累加的运行毛额（每个收入行算完才累加），更早的行读到的是不完整的毛额：
    /// 税行排在加班费行之前，就会对不含加班的毛额计税，而它既不报错也不留痕。
    /// 计算端用它兜底：规则之外落库的结构（或规则生效之前就已存在的结构）也必须挡在过账前面。
    /// </remarks>
    public static string? DescribeGrossOrderViolation(
        IReadOnlyCollection<LineSpec> lines,
        IReadOnlyDictionary<Guid, SalaryComponent> components,
        ISalaryFormulaEvaluator evaluator)
    {
        Check.NotNull(lines);
        Check.NotNull(components);
        Check.NotNull(evaluator);

        var orderedLines = lines.OrderBy(l => l.Sequence).ToList();
        var lastEarningSequence = LastEarningSequence(orderedLines, components);
        if (!lastEarningSequence.HasValue)
            return null;

        foreach (var line in orderedLines)
        {
            if (!components.TryGetValue(line.ComponentId, out var component))
                continue;

            var (formula, condition) = EffectiveExpressions(line, component);
            if (ReadsGrossTooEarly(evaluator, line, formula, condition, lastEarningSequence))
                return DescribeGrossReadTooEarly(line.Sequence, component.Code);
        }

        return null;
    }

    /// <summary>
    /// 第一条编码与计算钩子声明的外部变量撞名的行（有则给出错误文案，null = 没有）。
    /// </summary>
    /// <remarks>
    /// ★ 撞名是<b>静默错账</b>：钩子在按序求值前注入标量，组件行算完把同名键覆盖成本行金额，于是读这个名字的
    /// 公式拿到的是费率还是金额，取决于它排在第几行。组件保存时会拦，但钩子是后来的：国家包或消费方的钩子升级、
    /// 新声明了一个与既有组件同名的变量，既有组件不会被重新保存。所以结构保存、组件回查与计算端都要再查一次；
    /// 计算端用它兜底落 <c>CalculationError</c>，批次因此过不了账。
    /// </remarks>
    public static string? DescribeExternalVariableCollision(
        IReadOnlyCollection<LineSpec> lines,
        IReadOnlyDictionary<Guid, SalaryComponent> components,
        IReadOnlySet<string> externalVariables)
    {
        Check.NotNull(lines);
        Check.NotNull(components);
        Check.NotNull(externalVariables);

        if (externalVariables.Count == 0)
            return null;

        foreach (var line in lines.OrderBy(l => l.Sequence))
        {
            if (components.TryGetValue(line.ComponentId, out var component) && externalVariables.Contains(component.Code))
                return DescribeExternalVariableCollision(line.Sequence, component.Code);
        }

        return null;
    }

    private static string DescribeExternalVariableCollision(int sequence, string componentCode)
        => $"Line {sequence} (component '{componentCode}') has the same code as a run-time variable provided by a registered " +
            "calculation hook, so formulas reading it would get the hook's value or this line's amount depending on their position. " +
            "Rename the component, or drop the name from IPayslipCalculationHook.ProvidedVariables.";

    private static (string? Formula, string? Condition) EffectiveExpressions(LineSpec line, SalaryComponent component)
        => (string.IsNullOrWhiteSpace(line.FormulaOverride) ? component.Formula : line.FormulaOverride,
            string.IsNullOrWhiteSpace(line.ConditionOverride) ? component.Condition : line.ConditionOverride);

    private static int? LastEarningSequence(IEnumerable<LineSpec> orderedLines, IReadOnlyDictionary<Guid, SalaryComponent> components)
        => orderedLines
            .Where(l => components.TryGetValue(l.ComponentId, out var c) && c.Type == SalaryComponentType.Earning)
            .Select(l => (int?)l.Sequence)
            .LastOrDefault();

    private static bool ReadsGrossTooEarly(
        ISalaryFormulaEvaluator evaluator, LineSpec line, string? formula, string? condition, int? lastEarningSequence)
        => lastEarningSequence.HasValue && line.Sequence < lastEarningSequence.Value
            && (ReferencesGross(evaluator, formula) || ReferencesGross(evaluator, condition));

    private static string DescribeGrossReadTooEarly(int sequence, string componentCode)
        => $"Line {sequence} (component '{componentCode}') references {PayrollFormulaVariables.Gross}, " +
            "but earning lines follow it at a higher sequence, so GROSS would be incomplete. " +
            "Place all earning lines before any line that reads GROSS.";

    private static bool ReferencesGross(ISalaryFormulaEvaluator evaluator, string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var variables = evaluator.GetVariables(expression);
        return variables.Succeeded && variables.Data!.Contains(PayrollFormulaVariables.Gross);
    }

    private static Result ValidateExpression(
        ISalaryFormulaEvaluator evaluator, string? expression, IReadOnlySet<string> allowed, string componentCode, string kind)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return Result.Success();

        var variables = evaluator.GetVariables(expression);
        if (!variables.Succeeded)
            return Result.Failure($"Invalid {kind} for component '{componentCode}': {variables.Message}", 400);

        foreach (var variable in variables.Data!)
        {
            if (!allowed.Contains(variable))
            {
                return Result.Failure(
                    $"The {kind} for component '{componentCode}' references '{variable}', which is not a built-in variable, " +
                    "the code of an earlier line, or a variable declared by a registered calculation hook. " +
                    "Components can only reference components with a smaller sequence; to supply a value at run time, " +
                    "declare the name in IPayslipCalculationHook.ProvidedVariables and set it in BeforeCalculateAsync.", 400);
            }
        }

        return Result.Success();
    }
}
