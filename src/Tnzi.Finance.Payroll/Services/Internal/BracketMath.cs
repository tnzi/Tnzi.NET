namespace Tnzi.Finance.Payroll.Services.Internal;

/// <summary>
/// 税级表求税纯函数（行须已按序号升序且通过保存期连续性校验）
/// </summary>
/// <remarks>
/// 定位含 amount 的行（[LowerBound, UpperBound) 语义）：
/// 该行带速算扣除数 → <c>amount × Rate − QuickDeduction</c>（中式速算）；
/// 否则逐级累进求和（西式 piecewise）。两种表达对一致的表数值等价。
/// 返回原始 decimal，不做舍入——组件级舍入由计算器统一执行。
/// </remarks>
public static class BracketMath
{
    /// <summary>
    /// 按税级表行计算 amount 的税额。amount ≤ 0 返回 0；
    /// amount 超出表覆盖区间（末行有界且 amount ≥ 其上界）抛 <see cref="InvalidOperationException"/>
    /// </summary>
    public static decimal Calculate(IReadOnlyList<BracketRowDto> rows, decimal amount)
    {
        Check.NotNullOrEmpty(rows);

        if (amount <= 0)
            return 0m;

        BracketRowDto? hit = null;
        foreach (var row in rows)
        {
            if (amount >= row.LowerBound && (row.UpperBound == null || amount < row.UpperBound.Value))
            {
                hit = row;
                break;
            }
        }

        if (hit == null)
            throw new InvalidOperationException($"Amount {amount} falls outside the bracket table range.");

        if (hit.QuickDeduction.HasValue)
            return amount * hit.Rate - hit.QuickDeduction.Value;

        var tax = 0m;
        foreach (var row in rows)
        {
            if (row.LowerBound >= amount)
                break;

            var upper = row.UpperBound.HasValue ? Math.Min(row.UpperBound.Value, amount) : amount;
            tax += (upper - row.LowerBound) * row.Rate;
        }

        return tax;
    }
}
