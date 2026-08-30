namespace Tnzi.Finance.Payroll.Services;

/// <summary>
/// 薪资公式求值器（NCalc 的安全封装；country pack 计算钩子可复用）
/// </summary>
/// <remarks>
/// 安全约束：decimal 原生运算、invariant culture、函数白名单
/// （Bracket/Ytd/Attr/AttrText/Input + min/max/round/floor/ceiling/abs）、
/// 长度上限（PayrollOptions.FormulaMaxLength）、未知变量拒绝。
/// NCalc 类型不出现在本接口——实现可整体替换。
/// <para>
/// Bracket()/Ytd() 的数据源经 <see cref="SalaryFormulaContext"/> 的同步回调注入：
/// 求值器保持同步纯函数语义，调用方（P4c 的 PayslipCalculator / 单元测试）预取
/// 税级表与 YTD 聚合后以闭包喂入，未注入回调时调用对应函数返回失败 Result。
/// </para>
/// </remarks>
public interface ISalaryFormulaEvaluator
{
    /// <summary>求值金额公式（结果为数值）</summary>
    Result<decimal> Evaluate(string formula, SalaryFormulaContext context);

    /// <summary>求值条件表达式（结果必须为布尔）</summary>
    Result<bool> EvaluateCondition(string condition, SalaryFormulaContext context);

    /// <summary>
    /// 提取表达式引用的变量名（去重）。
    /// 同时执行语法校验与函数白名单校验——组件/结构保存期的静态检查入口。
    /// </summary>
    Result<IReadOnlyCollection<string>> GetVariables(string expression);

    /// <summary>
    /// 提取表达式调用的函数名（去重，与 <see cref="GetVariables"/> 同一次解析的另一半）。
    /// </summary>
    /// <remarks>
    /// 用途是"这条公式到底读不读某个函数"的静态判定。当前的调用方是一次性输入的录入端：
    /// 组件的生效公式若从不调用 <c>Input()</c>，那么为它录入的金额**永远不会被用到**，
    /// 而一笔录进去却不生效的奖金比一个报错危险得多——所以录入端据此 400 而不是照单收下。
    /// </remarks>
    Result<IReadOnlyCollection<string>> GetFunctions(string expression);
}

/// <summary>
/// 薪资公式求值上下文
/// </summary>
public class SalaryFormulaContext
{
    /// <summary>
    /// 变量表（内置变量 + 已算组件按 Code）。
    /// 键约定与组件 Code 同为大写规范形（Ordinal 比较）。
    /// </summary>
    public IReadOnlyDictionary<string, decimal> Variables { get; init; } =
        new Dictionary<string, decimal>(StringComparer.Ordinal);

    /// <summary>
    /// 员工扩展属性（Attr()/AttrText() 数据源；建议使用忽略大小写的字典，
    /// 见 PayrollAttributeHelper.Parse）
    /// </summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Bracket(tableCode, amount) 回调：按表编码与金额求税。
    /// null 时公式调用 Bracket() 返回失败 Result。
    /// </summary>
    public Func<string, decimal, decimal>? BracketResolver { get; init; }

    /// <summary>
    /// Ytd(componentCode) 回调：该组件年度内已过账累计。
    /// null 时公式调用 Ytd() 返回失败 Result。
    /// </summary>
    public Func<string, decimal>? YtdResolver { get; init; }

    /// <summary>
    /// 本次求值所属组件的一次性输入金额（<c>Input()</c> 的返回值）。
    /// null = 本期没有为这个组件录入过——与 Bracket/Ytd 不同，这是**正常状态**
    /// （绝大多数员工绝大多数期间都没有奖金），所以 <c>Input()</c> 取默认值而不是报错。
    /// </summary>
    /// <remarks>
    /// 因为它绑定到"当前这一行"，调用方需按行构造上下文（<c>Variables</c> 等仍可共享同一引用）。
    /// </remarks>
    public decimal? InputAmount { get; init; }
}
