namespace Tnzi.Finance.Payroll.Metadata;

/// <summary>
/// 薪资公式里可用的自定义函数名（内置数学函数不在此列）。
/// </summary>
/// <remarks>
/// 名字有三处消费：求值器的函数白名单、求值时的自定义函数分派，以及"这条公式读不读某个
/// 函数"的静态判定（<see cref="Services.ISalaryFormulaEvaluator.GetFunctions"/> 的调用方，
/// 当前是一次性输入的录入端与计算端）。
/// 放进常量是为了让三处不可能漂移 —— 字面量各写一份时，改了一处而漏了另一处，
/// 最好的情形是求值期报"未知函数"，最坏的情形是一条**校验静默失效**；两种都不是编译错误。
/// </remarks>
public static class PayrollFormulaFunctions
{
    /// <summary>Bracket(tableCode, amount) — 按税级表求税</summary>
    public const string Bracket = "Bracket";

    /// <summary>Ytd(componentCode) — 本年度已提交批次的累计（或 <c>#</c> 聚合键）</summary>
    public const string Ytd = "Ytd";

    /// <summary>Attr(name, default) — 员工扩展属性（数值）</summary>
    public const string Attr = "Attr";

    /// <summary>AttrText(name, default) — 员工扩展属性（文本）</summary>
    public const string AttrText = "AttrText";

    /// <summary>
    /// Input() / Input(default) — 本批次为本员工在**本组件**上录入的一次性金额
    /// （无录入时取 default，缺省 0）。
    /// </summary>
    /// <remarks>
    /// 刻意**不带组件编码参数**：一个组件读它自己的那一笔。于是没有第二个可拼错的名字空间
    /// —— 保存期"引用一个不存在的东西就 400"这条静态检查一分不丢，也不需要把每个新组件的
    /// 输入名预先声明在某个静态集合里（管理员运行时新建的扣款种类照样能用）。
    /// 需要"某一项输入参与另一项的计算"时，把它做成一个更早序号的
    /// <see cref="SalaryComponentType.Informational"/> 行，后续行按 Code 引用即可。
    /// </remarks>
    public const string Input = "Input";
}
