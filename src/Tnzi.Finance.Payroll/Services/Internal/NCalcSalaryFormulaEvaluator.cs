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
    /// 函数白名单：自定义 4 函数 + 内置数学函数子集（忽略大小写）
    /// </summary>
    private static readonly HashSet<string> AllowedFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bracket", "Ytd", "Attr", "AttrText",
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
            var parsed = new Expression(expression, EvaluationOptions, CultureInfo.InvariantCulture);

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
            var expression = new Expression(text, EvaluationOptions, CultureInfo.InvariantCulture);

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
        // NCalc 对每个函数调用都先触发本事件：命中 4 个自定义函数时给出结果，
        // 其余（min/max/round/... 白名单内置）不设置 Result，交回 NCalc 原生处理
        switch (name.ToUpperInvariant())
        {
            case "BRACKET":
                RequireArgs(args, 2, "Bracket(tableCode, amount)");
                if (context.BracketResolver == null)
                    throw new FormulaFunctionException("Bracket() is not available in this evaluation context.");
                args.Result = context.BracketResolver(
                    ToText(args.Parameters.Evaluate(0), "Bracket", "tableCode"),
                    ToDecimal(args.Parameters.Evaluate(1), "Bracket", "amount"));
                break;

            case "YTD":
                RequireArgs(args, 1, "Ytd(componentCode)");
                if (context.YtdResolver == null)
                    throw new FormulaFunctionException("Ytd() is not available in this evaluation context.");
                args.Result = context.YtdResolver(ToText(args.Parameters.Evaluate(0), "Ytd", "componentCode"));
                break;

            case "ATTR":
            {
                RequireArgs(args, 2, "Attr(name, default)");
                var attrName = ToText(args.Parameters.Evaluate(0), "Attr", "name");
                if (!context.Attributes.TryGetValue(attrName, out var raw))
                {
                    args.Result = ToDecimal(args.Parameters.Evaluate(1), "Attr", "default");
                    break;
                }

                if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                    throw new FormulaFunctionException($"Employee attribute '{attrName}' is not a number.");
                args.Result = parsed;
                break;
            }

            case "ATTRTEXT":
            {
                RequireArgs(args, 2, "AttrText(name, default)");
                var attrName = ToText(args.Parameters.Evaluate(0), "AttrText", "name");
                args.Result = context.Attributes.TryGetValue(attrName, out var text)
                    ? text
                    : ToText(args.Parameters.Evaluate(1), "AttrText", "default");
                break;
            }
        }
    }

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
