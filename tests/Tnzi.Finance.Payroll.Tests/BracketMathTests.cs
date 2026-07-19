namespace Tnzi.Finance.Payroll.Tests;

/// <summary>
/// 税级求税纯函数：速算 vs 累进等价 / [lower, upper) 边界 / 开区间末档
/// </summary>
public class BracketMathTests
{
    /// <summary>四档速算表（速算扣除数与逐级累进数值一致）</summary>
    private static List<BracketRowDto> QuickRows() =>
    [
        new() { Sequence = 1, LowerBound = 0, UpperBound = 3000, Rate = 0.03m, QuickDeduction = 0 },
        new() { Sequence = 2, LowerBound = 3000, UpperBound = 12000, Rate = 0.10m, QuickDeduction = 210 },
        new() { Sequence = 3, LowerBound = 12000, UpperBound = 25000, Rate = 0.20m, QuickDeduction = 1410 },
        new() { Sequence = 4, LowerBound = 25000, UpperBound = null, Rate = 0.25m, QuickDeduction = 2660 }
    ];

    /// <summary>同档次纯累进表</summary>
    private static List<BracketRowDto> ProgressiveRows() =>
    [
        new() { Sequence = 1, LowerBound = 0, UpperBound = 3000, Rate = 0.03m },
        new() { Sequence = 2, LowerBound = 3000, UpperBound = 12000, Rate = 0.10m },
        new() { Sequence = 3, LowerBound = 12000, UpperBound = 25000, Rate = 0.20m },
        new() { Sequence = 4, LowerBound = 25000, UpperBound = null, Rate = 0.25m }
    ];

    [Theory]
    [InlineData(1000)]
    [InlineData(2999.99)]
    [InlineData(3000)]
    [InlineData(11999)]
    [InlineData(12000)]
    [InlineData(20000)]
    [InlineData(25000)]
    [InlineData(30000)]
    [InlineData(100000)]
    public void QuickDeduction_And_Progressive_AreEquivalent(decimal amount)
    {
        var quick = BracketMath.Calculate(QuickRows(), amount);
        var progressive = BracketMath.Calculate(ProgressiveRows(), amount);
        quick.ShouldBe(progressive);
    }

    [Fact]
    public void KnownValues_AreCorrect()
    {
        // 12000：3000×3% + 9000×10% = 990（恰在第三档下界，速算 12000×20% − 1410 = 990）
        BracketMath.Calculate(QuickRows(), 12000m).ShouldBe(990m);
        // 20000：990 + 8000×20% = 2590
        BracketMath.Calculate(ProgressiveRows(), 20000m).ShouldBe(2590m);
        // 30000：990 + 13000×20% + 5000×25% = 4840
        BracketMath.Calculate(QuickRows(), 30000m).ShouldBe(4840m);
    }

    [Fact]
    public void UpperBound_IsExclusive_LowerBound_IsInclusive()
    {
        // 3000 恰为第二档下界：命中 10% 档（速算 3000×10% − 210 = 90 = 3000×3%）
        BracketMath.Calculate(QuickRows(), 3000m).ShouldBe(90m);
        // 2999.99 仍在第一档
        BracketMath.Calculate(QuickRows(), 2999.99m).ShouldBe(2999.99m * 0.03m);
    }

    [Fact]
    public void ZeroOrNegativeAmount_ReturnsZero()
    {
        BracketMath.Calculate(QuickRows(), 0m).ShouldBe(0m);
        BracketMath.Calculate(QuickRows(), -100m).ShouldBe(0m);
    }

    [Fact]
    public void OpenTopBracket_CoversLargeAmounts()
    {
        BracketMath.Calculate(ProgressiveRows(), 1000000m)
            .ShouldBe(90m + 900m + 2600m + (1000000m - 25000m) * 0.25m);
    }

    [Fact]
    public void BoundedTable_AmountAboveRange_Throws()
    {
        List<BracketRowDto> bounded =
        [
            new() { Sequence = 1, LowerBound = 0, UpperBound = 1000, Rate = 0.05m }
        ];

        Should.Throw<InvalidOperationException>(() => BracketMath.Calculate(bounded, 5000m));
    }
}
