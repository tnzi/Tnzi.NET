using NCalc;
using NCalc.Exceptions;

namespace Tnzi.Finance.Payroll.Services.Internal;

/// <summary>
/// <see cref="ISalaryFormulaEvaluator"/> 的 NCalc 实现
/// </summary>
/// <remarks>
/// 安全面：<see cref="ExpressionOptions.DecimalAsDefault"/>（decimal 原生，无浮点漂移）、
/// invariant culture、函数白名单（解析后先验 GetFunctionNames，未知函数在求值前拒绝）、
/// 未知变量在求值前拒绝、长度上限热读自 <c>PayrollOptions.FormulaMaxLength</c>。
/// 不注册任何逃逸函数；NCalc 类型不出公共 API。
/// </remarks>
public class NCalcSalaryFormulaEvaluator : ISalaryFormulaEvaluator
{
    private const ExpressionOptions EvaluationOptions =
        ExpressionOptions.DecimalAsDefault |
        ExpressionOptions.IgnoreCaseAtBuiltInFunctions |
        ExpressionOptions.RoundAwayFromZero;

    /// <summary>
    /// 解析 + 求值配置（NCalc 7 起由 <see cref="ExpressionConfiguration"/> 承载，
    /// 与每次求值的运行时状态 <c>ExpressionContext</c> 分离）
    /// </summary>
    /// <remarks>
    /// <see cref="ExpressionConfiguration"/> 的属性全为 init-only，故可安全共享一份静态实例；
    /// 每个 <see cref="Expression"/> 仍各自持有自己的参数与函数处理器。
    /// </remarks>
    private static readonly ExpressionConfiguration FormulaConfiguration =
        ExpressionConfiguration.FromOptions(EvaluationOptions);

    /// <summary>
    /// 函数白名单：自定义 5 函数 + 内置数学函数子集（忽略大小写）
    /// </summary>
    private static readonly HashSet<string> AllowedFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        PayrollFormulaFunctions.Bracket, PayrollFormulaFunctions.Ytd,
        PayrollFormulaFunctions.Attr, PayrollFormulaFunctions.AttrText,
        PayrollFormulaFunctions.Input,
        "Min", "Max", "Round", "Floor", "Ceiling", "Abs"
    };

    private readonly IOptionsSnapshot<PayrollOptions> _options;

    public NCalcSalaryFormulaEvaluator(IOptionsSnapshot<PayrollOptions> options)
    {
        _options = Check.NotNull(options);
    }

    public Result<decimal> Evaluate(string formula, SalaryFormulaContext context)
    {
        Check.NotNull(context);

        var evaluated = EvaluateCore(formula, context, "formula");
        if (!evaluated.Succeeded)
            return Result.Failure<decimal>(evaluated.Message ?? "Formula evaluation failed.", evaluated.Code ?? 400);

        var value = evaluated.Data;
        if (value is bool)
            return Result.Failure<decimal>("The formula must evaluate to a number, not a boolean.", 400);

        try
        {
            return Result.Success(Convert.ToDecimal(value, CultureInfo.InvariantCulture));
        }
        catch (Exception)
        {
            return Result.Failure<decimal>("The formula did not evaluate to a number.", 400);
        }
    }

    public Result<bool> EvaluateCondition(string condition, SalaryFormulaContext context)
    {
        Check.NotNull(context);

        var evaluated = EvaluateCore(condition, context, "condition");
        if (!evaluated.Succeeded)
            return Result.Failure<bool>(evaluated.Message ?? "Condition evaluation failed.", evaluated.Code ?? 400);

        if (evaluated.Data is bool result)
            return Result.Success(result);

        return Result.Failure<bool>("The condition must evaluate to a boolean.", 400);
    }

    public Result<IReadOnlyCollection<string>> GetVariables(string expression)
    {
        var lengthCheck = CheckLength(expression, "expression");
        if (!lengthCheck.Succeeded)
            return Result.Failure<IReadOnlyCollection<string>>(lengthCheck.Message!, lengthCheck.Code ?? 400);

        try
        {
            var parsed = new Expression(expression, FormulaConfiguration, cultureInfo: CultureInfo.InvariantCulture);

            var functionCheck = CheckFunctions(parsed);
            if (!functionCheck.Succeeded)
                return Result.Failure<IReadOnlyCollection<string>>(functionCheck.Message!, functionCheck.Code ?? 400);

            IReadOnlyCollection<string> names = parsed.GetParameterNames()
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return Result.Success(names);
        }
        catch (NCalcException ex)
        {
            return Result.Failure<IReadOnlyCollection<string>>($"Invalid expression: {RootMessage(ex)}", 400);
        }
    }

    public Result<IReadOnlyCollection<string>> GetFunctions(string expression)
    {
        var lengthCheck = CheckLength(expression, "expression");
        if (!lengthCheck.Succeeded)
            return Result.Failure<IReadOnlyCollection<string>>(lengthCheck.Message!, lengthCheck.Code ?? 400);

        try
        {
            var parsed = new Expression(expression, FormulaConfiguration, cultureInfo: CultureInfo.InvariantCulture);

            var functionCheck = CheckFunctions(parsed);
            if (!functionCheck.Succeeded)
                return Result.Failure<IReadOnlyCollection<string>>(functionCheck.Message!, functionCheck.Code ?? 400);

            // 函数名的比较口径与白名单一致（NCalc 以 IgnoreCaseAtBuiltInFunctions 求值，
            // 自定义函数在 handler 里也按大写归一），故返回忽略大小写的集合。
            IReadOnlyCollection<string> names = parsed.GetFunctionNames()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Result.Success(names);
        }
        catch (NCalcException ex)
        {
            return Result.Failure<IReadOnlyCollection<string>>($"Invalid expression: {RootMessage(ex)}", 400);
        }
    }

    /// <summary>
    /// 解析 + 白名单/变量预检 + 求值。任何失败路径都以 Result 返回（不外抛）
    /// </summary>
    private Result<object?> EvaluateCore(string text, SalaryFormulaContext context, string kind)
    {
        var lengthCheck = CheckLength(text, kind);
        if (!lengthCheck.Succeeded)
            return Result.Failure<object?>(lengthCheck.Message!, lengthCheck.Code ?? 400);

        try
        {
            var expression = new Expression(text, FormulaConfiguration, cultureInfo: CultureInfo.InvariantCulture);

            var functionCheck = CheckFunctions(expression);
            if (!functionCheck.Succeeded)
                return Result.Failure<object?>(functionCheck.Message!, functionCheck.Code ?? 400);

            foreach (var parameter in expression.GetParameterNames().Distinct(StringComparer.Ordinal))
            {
                if (!context.Variables.ContainsKey(parameter))
                    return Result.Failure<object?>($"Unknown variable '{parameter}' in the {kind}.", 400);
            }

            foreach (var variable in context.Variables)
                expression.Parameters[variable.Key] = variable.Value;

            expression.EvaluateFunction += (name, args) => EvaluateCustomFunction(name, args, context);

            return Result.Success<object?>(expression.Evaluate());
        }
        catch (FormulaFunctionException ex)
        {
            return Result.Failure<object?>(ex.Message, 400);
        }
        catch (Exception ex)
        {
            return Result.Failure<object?>($"Invalid {kind}: {RootMessage(ex)}", 400);
        }
    }

    private Result CheckLength(string? text, string kind)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Result.Failure($"The {kind} is empty.", 400);

        var maxLength = _options.Value.FormulaMaxLength;
        if (text.Length > maxLength)
            return Result.Failure($"The {kind} exceeds the maximum length of {maxLength} characters.", 400);

        return Result.Success();
    }

    private static Result CheckFunctions(Expression expression)
    {
        foreach (var function in expression.GetFunctionNames())
        {
            if (!AllowedFunctions.Contains(function))
                return Result.Failure($"Function '{function}' is not allowed in salary formulas.", 400);
        }

        return Result.Success();
    }

    private static void EvaluateCustomFunction(string name, NCalc.Handlers.FunctionEventArgs args, SalaryFormulaContext context)
    {
        // NCalc 对每个函数调用都先触发本事件：命中 5 个自定义函数时给出结果，
        // 其余（min/max/round/... 白名单内置）不设置 Result，交回 NCalc 原生处理。
        // ★分派键取自 PayrollFormulaFunctions 而不是字面量：白名单、静态判定、这里的分派
        // 是同一个名字的三处消费，字面量各写一份时改了一处漏了另一处不会是编译错误。
        if (Is(name, PayrollFormulaFunctions.Input))
        {
            // 没录入过就是没录入过——这是绝大多数员工绝大多数期间的正常状态，
            // 所以取默认值（缺省 0）而不是像 Bracket()/Ytd() 那样报"上下文不可用"。
            if (args.Parameters.Count > 1)
                throw new FormulaFunctionException("Input() takes no arguments, or a single default value: Input() / Input(default).");
            args.Result = context.InputAmount
                ?? (args.Parameters.Count == 1
                    ? ToDecimal(args.Parameters.Evaluate(0), "Input", "default")
                    : 0m);
            return;
        }

        if (Is(name, PayrollFormulaFunctions.Bracket))
        {
            RequireArgs(args, 2, "Bracket(tableCode, amount)");
            if (context.BracketResolver == null)
                throw new FormulaFunctionException("Bracket() is not available in this evaluation context.");
            args.Result = context.BracketResolver(
                ToText(args.Parameters.Evaluate(0), "Bracket", "tableCode"),
                ToDecimal(args.Parameters.Evaluate(1), "Bracket", "amount"));
            return;
        }

        if (Is(name, PayrollFormulaFunctions.Ytd))
        {
            RequireArgs(args, 1, "Ytd(componentCode)");
            if (context.YtdResolver == null)
                throw new FormulaFunctionException("Ytd() is not available in this evaluation context.");
            var ytdKey = ToText(args.Parameters.Evaluate(0), "Ytd", "componentCode");
            // 组件编码查不到就是 0（这个员工今年确实还没有过这一项）。而 `#` 命名空间
            // 是**封闭的**：查不到只可能是拼错，静默返回 0 会让一个法定上限的基数变成零，
            // 于是上限永不触发——要到年终对账才看得出来。
            var normalized = ytdKey.Trim().ToUpperInvariant();
            if (normalized.StartsWith('#') && !PayrollYtdAggregates.All.Contains(normalized))
            {
                throw new FormulaFunctionException(
                    $"Ytd('{ytdKey}') is not a known aggregate. Valid aggregates: {string.Join(", ", PayrollYtdAggregates.All)}.");
            }
            args.Result = context.YtdResolver(ytdKey);
            return;
        }

        if (Is(name, PayrollFormulaFunctions.Attr))
        {
            RequireArgs(args, 2, "Attr(name, default)");
            var attrName = ToText(args.Parameters.Evaluate(0), "Attr", "name");
            if (!context.Attributes.TryGetValue(attrName, out var raw))
            {
                args.Result = ToDecimal(args.Parameters.Evaluate(1), "Attr", "default");
                return;
            }

            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                throw new FormulaFunctionException($"Employee attribute '{attrName}' is not a number.");
            args.Result = parsed;
            return;
        }

        if (Is(name, PayrollFormulaFunctions.AttrText))
        {
            RequireArgs(args, 2, "AttrText(name, default)");
            var attrName = ToText(args.Parameters.Evaluate(0), "AttrText", "name");
            args.Result = context.Attributes.TryGetValue(attrName, out var text)
                ? text
                : ToText(args.Parameters.Evaluate(1), "AttrText", "default");
        }
    }

    /// <summary>函数名比较口径与白名单一致（NCalc 内置函数本就忽略大小写）</summary>
    private static bool Is(string name, string function)
        => string.Equals(name, function, StringComparison.OrdinalIgnoreCase);

    private static void RequireArgs(NCalc.Handlers.FunctionEventArgs args, int count, string signature)
    {
        if (args.Parameters.Count != count)
            throw new FormulaFunctionException($"{signature} requires exactly {count} argument(s).");
    }

    private static string ToText(object? value, string function, string argument)
    {
        if (value is string text)
            return text;
        throw new FormulaFunctionException($"{function}() requires a text value for '{argument}'.");
    }

    private static decimal ToDecimal(object? value, string function, string argument)
    {
        try
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            throw new FormulaFunctionException($"{function}() requires a numeric value for '{argument}'.");
        }
    }

    private static string RootMessage(Exception exception)
    {
        var current = exception;
        while (current.InnerException != null)
            current = current.InnerException;
        return current.Message;
    }

    /// <summary>
    /// 自定义函数内部的业务失败信号（缺回调/参数类型不符等），
    /// 由 <see cref="EvaluateCore"/> 捕获转换为失败 Result
    /// </summary>
    private sealed class FormulaFunctionException : Exception
    {
        public FormulaFunctionException(string message) : base(message)
        {
        }
    }
}
