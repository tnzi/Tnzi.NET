namespace Tnzi.Finance.Payroll.Services.Internal;

/// <summary>
/// <see cref="Payslip"/> 文本字段的列宽，以及越界时该怎么办。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>失败路径是最不能再失败一次的地方</b>：<see cref="Payslip.CalculationError"/> 的来源没有一个有上界 ——
/// 孤儿输入清单按笔数增长、消费方钩子的 <c>Result.Message</c> 原样透传、求值器的失败裹着公式原文
/// （<c>FormulaMaxLength</c> 默认 2000）。原样赋值到一个 1000 字符的列，SQL Server / PostgreSQL 上
/// <c>CalculateAsync</c> 的 <c>InsertManyAsync</c> 抛 <c>DbUpdateException</c>，整批回滚 500，批次停在 Draft
/// 且没有任何解释：那条本该把问题摆到操作员面前的消息，正是存不进去的东西。「Error 不炸批」在这里被
/// 错误记录本身炸掉了。SQLite 不检查列宽，所以本仓的测试只能靠长度断言看见它。
/// </para>
/// <para>
/// 方向是<b>收敛，绝不失败</b>：消息是给人看的，留开头比整条丢掉有用。
/// 常量由 <c>PayslipConfiguration</c> 直接引用，两处不可能漂移。手法与 Banking 的 <c>ReceiptFieldLimits</c> 一致。
/// </para>
/// </remarks>
internal static class PayslipFieldLimits
{
    /// <summary><see cref="Payslip.CalculationError"/> 列宽</summary>
    internal const int CalculationErrorMaxLength = 1000;

    /// <summary>
    /// 孤儿输入消息里逐笔列出的上限；其余汇总成「and N more」。
    /// </summary>
    /// <remarks>
    /// 截断只保证存得进去，不保证读得懂 —— 一条在第 7 笔中间被切断的清单，操作员既不知道还有几笔、
    /// 也不知道最后那笔是谁。先汇总再截断，截断就成了几乎到不了的兜底。
    /// </remarks>
    internal const int OrphanedInputsListed = 5;

    private const string TruncationMarker = "...";

    /// <summary>
    /// 把一条计算错误收敛到 <see cref="Payslip.CalculationError"/> 装得下的长度；超长时末尾带省略标记。
    /// </summary>
    internal static string? ClampCalculationError(string? error)
    {
        if (error == null || error.Length <= CalculationErrorMaxLength)
            return error;

        var end = CalculationErrorMaxLength - TruncationMarker.Length;
        // 不留下半个代理对：孤立的高代理项是非法 UTF-16，PostgreSQL 编码时会拒绝整条插入，
        // 一个防「写库失败」的方法自己造出写库失败就没有意义了。
        if (char.IsHighSurrogate(error[end - 1]))
            end--;

        return error[..end] + TruncationMarker;
    }
}
