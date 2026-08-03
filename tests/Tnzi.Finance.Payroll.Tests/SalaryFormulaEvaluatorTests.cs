using Microsoft.Extensions.Options;

namespace Tnzi.Finance.Payroll.Tests;

/// <summary>
/// 公式求值器：decimal 精度 / 函数白名单 / 长度上限 / invariant culture / 注入面拒绝 / 自定义函数回调
/// </summary>
public class SalaryFormulaEvaluatorTests
{
    private static NCalcSalaryFormulaEvaluator CreateEvaluator(int maxLength = 2000)
        => new(new OptionsSnapshotStub(new PayrollOptions { FormulaMaxLength = maxLength }));

    private static SalaryFormulaContext EmptyContext() => new();

    private static SalaryFormulaContext ContextWith(params (string Name, decimal Value)[] variables)
        => new() { Variables = variables.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal) };

    [Fact]
    public void Evaluate_DecimalArithmetic_IsExact()
    {
        // double 语义下 0.1 + 0.2 = 0.30000000000000004；decimal 原生必须精确
        var result = CreateEvaluator().Evaluate("0.1 + 0.2", EmptyContext());
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(0.3m);
    }

    [Fact]
    public void Evaluate_VariableSubstitution_Works()
    {
        var result = CreateEvaluator().Evaluate("BASE * 0.075", ContextWith(("BASE", 63000m)));
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(4725m);
    }

    [Fact]
    public void Evaluate_UnderscoredVariables_Work()
    {
        var context = ContextWith(("WORKED_DAYS", 20m), ("PERIOD_DAYS", 30m), ("BASE", 3000m));
        var result = CreateEvaluator().Evaluate("BASE * WORKED_DAYS / PERIOD_DAYS", context);
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(2000m);
    }

    [Fact]
    public void Evaluate_WhitelistedBuiltIns_Work()
    {
        var evaluator = CreateEvaluator();
        evaluator.Evaluate("min(3, 5)", EmptyContext()).Data.ShouldBe(3m);
        evaluator.Evaluate("max(3, 5)", EmptyContext()).Data.ShouldBe(5m);
        evaluator.Evaluate("floor(2.9)", EmptyContext()).Data.ShouldBe(2m);
        evaluator.Evaluate("ceiling(2.1)", EmptyContext()).Data.ShouldBe(3m);
        evaluator.Evaluate("abs(-5)", EmptyContext()).Data.ShouldBe(5m);
        evaluator.Evaluate("round(10 / 4.0, 1)", EmptyContext()).Data.ShouldBe(2.5m);
    }

    [Fact]
    public void Evaluate_Round_IsAwayFromZero()
    {
        // 财务语义：2.5 → 3（银行家舍入会得 2）
        var result = CreateEvaluator().Evaluate("round(2.5, 0)", EmptyContext());
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(3m);
    }

    [Fact]
    public void Evaluate_NonWhitelistedFunction_Rejected()
    {
        var result = CreateEvaluator().Evaluate("Pow(2, 3)", EmptyContext());
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("Pow");
    }

    [Fact]
    public void Evaluate_IfFunction_Rejected()
    {
        // 条件逻辑走 Condition 字段，公式内禁 if——白名单外一律拒绝
        CreateEvaluator().Evaluate("if(1 > 0, 1, 2)", EmptyContext()).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void Evaluate_UnknownVariable_Rejected()
    {
        var result = CreateEvaluator().Evaluate("BASE + UNKNOWN_VAR", ContextWith(("BASE", 100m)));
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("UNKNOWN_VAR");
    }

    [Fact]
    public void Evaluate_ExceedingMaxLength_Rejected()
    {
        var evaluator = CreateEvaluator(maxLength: 50);
        var formula = "1" + string.Concat(Enumerable.Repeat(" + 1", 20));
        formula.Length.ShouldBeGreaterThan(50);

        var result = evaluator.Evaluate(formula, EmptyContext());
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("maximum length");
    }

    [Fact]
    public void Evaluate_EmptyFormula_Rejected()
    {
        CreateEvaluator().Evaluate("  ", EmptyContext()).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void Evaluate_MalformedExpression_Rejected()
    {
        CreateEvaluator().Evaluate("1 +", EmptyContext()).Succeeded.ShouldBeFalse();
        CreateEvaluator().Evaluate("1; 2", EmptyContext()).Succeeded.ShouldBeFalse();
        CreateEvaluator().Evaluate("System.IO.File", EmptyContext()).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void Evaluate_IsCultureInvariant()
    {
        // de-DE 里 "," 是小数点；invariant 求值必须不受当前线程 culture 影响
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var result = CreateEvaluator().Evaluate("1.5 * 2", EmptyContext());
            result.Succeeded.ShouldBeTrue(result.Message);
            result.Data.ShouldBe(3.0m);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Evaluate_BooleanResult_Rejected()
    {
        CreateEvaluator().Evaluate("1 > 0", EmptyContext()).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void EvaluateCondition_Works()
    {
        var evaluator = CreateEvaluator();
        evaluator.EvaluateCondition("BASE > 1000", ContextWith(("BASE", 5000m))).Data.ShouldBeTrue();
        evaluator.EvaluateCondition("BASE > 1000", ContextWith(("BASE", 500m))).Data.ShouldBeFalse();
    }

    [Fact]
    public void EvaluateCondition_NonBooleanResult_Rejected()
    {
        var result = CreateEvaluator().EvaluateCondition("1 + 1", EmptyContext());
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("boolean");
    }

    [Fact]
    public void Bracket_WithoutResolver_Fails()
    {
        var result = CreateEvaluator().Evaluate("Bracket('T1', 100)", EmptyContext());
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("Bracket()");
    }

    [Fact]
    public void Bracket_WithResolver_Works()
    {
        var context = new SalaryFormulaContext
        {
            Variables = new Dictionary<string, decimal>(StringComparer.Ordinal) { ["GROSS"] = 20000m },
            BracketResolver = (code, amount) =>
            {
                code.ShouldBe("T1");
                return amount * 0.1m;
            }
        };

        var result = CreateEvaluator().Evaluate("Bracket('T1', GROSS)", context);
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(2000m);
    }

    [Fact]
    public void Ytd_WithoutResolver_Fails()
    {
        CreateEvaluator().Evaluate("Ytd('BONUS')", EmptyContext()).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void Ytd_WithResolver_Works()
    {
        var context = new SalaryFormulaContext { YtdResolver = code => code == "BONUS" ? 8000m : 0m };
        var result = CreateEvaluator().Evaluate("min(Ytd('BONUS') + 1000, 10000)", context);
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(9000m);
    }

    [Fact]
    public void Attr_ReturnsAttributeOrDefault()
    {
        var context = new SalaryFormulaContext
        {
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["DEPENDENTS"] = "2" }
        };

        var evaluator = CreateEvaluator();
        evaluator.Evaluate("Attr('DEPENDENTS', 0) * 1000", context).Data.ShouldBe(2000m);
        evaluator.Evaluate("Attr('MISSING', 5)", context).Data.ShouldBe(5m);
        // 属性名忽略大小写
        evaluator.Evaluate("Attr('dependents', 0)", context).Data.ShouldBe(2m);
    }

    [Fact]
    public void Attr_NonNumericAttribute_Fails()
    {
        var context = new SalaryFormulaContext
        {
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["TAX_STATUS"] = "SINGLE" }
        };

        var result = CreateEvaluator().Evaluate("Attr('TAX_STATUS', 0)", context);
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("TAX_STATUS");
    }

    [Fact]
    public void AttrText_WorksInConditions()
    {
        var context = new SalaryFormulaContext
        {
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["TAX_STATUS"] = "MARRIED" }
        };

        var evaluator = CreateEvaluator();
        evaluator.EvaluateCondition("AttrText('TAX_STATUS', 'SINGLE') == 'MARRIED'", context).Data.ShouldBeTrue();
        evaluator.EvaluateCondition("AttrText('MISSING', 'SINGLE') == 'SINGLE'", context).Data.ShouldBeTrue();
    }

    [Fact]
    public void CustomFunction_WrongArity_Fails()
    {
        var context = new SalaryFormulaContext { BracketResolver = (_, _) => 0m };
        var result = CreateEvaluator().Evaluate("Bracket('T1')", context);
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("argument");
    }

    [Fact]
    public void GetVariables_ReturnsDistinctNames()
    {
        var result = CreateEvaluator().GetVariables("BASIC * 0.4 + WORKED_DAYS + BASIC");
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(["BASIC", "WORKED_DAYS"], ignoreOrder: true);
    }

    [Fact]
    public void GetVariables_MalformedExpression_Fails()
    {
        CreateEvaluator().GetVariables("1 +").Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void GetVariables_NonWhitelistedFunction_Fails()
    {
        var result = CreateEvaluator().GetVariables("Sqrt(BASE)");
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("Sqrt");
    }

    private sealed class OptionsSnapshotStub : IOptionsSnapshot<PayrollOptions>
    {
        private readonly PayrollOptions _value;

        public OptionsSnapshotStub(PayrollOptions value) => _value = value;

        public PayrollOptions Value => _value;

        public PayrollOptions Get(string? name) => _value;
    }

    [Fact]
    public void Ytd_UnknownAggregateKey_Fails()
    {
        // `#` 是封闭命名空间：查不到只可能是拼错。静默返回 0 会让一个法定上限的
        // 基数变成零 —— 上限永不触发，而且要到年终对账才看得出来。
        var context = new SalaryFormulaContext { YtdResolver = _ => 0m };
        var result = CreateEvaluator().Evaluate("Ytd('#GROSSS')", context);
        result.Succeeded.ShouldBeFalse();
        result.Message!.ShouldContain("#GROSS");
    }

    [Fact]
    public void Ytd_KnownAggregateKey_ReachesTheResolver()
    {
        var context = new SalaryFormulaContext
        {
            YtdResolver = code => code == PayrollYtdAggregates.Gross ? 61500m : 0m
        };
        var result = CreateEvaluator().Evaluate("max(0, min(Ytd('#GROSS') + 5000, 73200) - 68500)", context);
        result.Succeeded.ShouldBeTrue(result.Message);
        // CPP2 形状：min(66500, 73200) - 68500 < 0 → 0
        result.Data.ShouldBe(0m);
    }

    [Fact]
    public void Ytd_ComponentCodeMiss_StillReturnsZero_NotAnError()
    {
        // 组件编码查不到是正常的：这个员工今年确实还没有过这一项。
        var context = new SalaryFormulaContext { YtdResolver = _ => 0m };
        var result = CreateEvaluator().Evaluate("Ytd('NEVER_PAID')", context);
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(0m);
    }
}
