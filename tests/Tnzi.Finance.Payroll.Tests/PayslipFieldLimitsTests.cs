namespace Tnzi.Finance.Payroll.Tests;

/// <summary>
/// <see cref="PayslipFieldLimits"/>：<c>Payslip.CalculationError</c> 的列宽收敛。
/// </summary>
/// <remarks>
/// 被保护的缺陷：<c>PayslipConfiguration</c> 把 <c>CalculationError</c> 钉在 1000 字符，而计算器原样赋值 ——
/// 孤儿输入清单、钩子的错误消息、求值器裹着公式原文的失败都没有上界。SQL Server / PostgreSQL 上超宽即
/// <c>DbUpdateException</c>，整批回滚 500；「把问题摆到操作员面前」的机制自己炸掉了。SQLite 不检查列宽，
/// 所以只有长度断言能在本仓看见它。
/// </remarks>
public class PayslipFieldLimitsTests
{
    [Fact]
    public void ColumnWidth_IsTheConstant()
    {
        PayslipFieldLimits.CalculationErrorMaxLength.ShouldBe(1000);
    }

    [Fact]
    public void ClampCalculationError_ShortText_IsUnchanged()
    {
        PayslipFieldLimits.ClampCalculationError("Net pay is negative.").ShouldBe("Net pay is negative.");
        PayslipFieldLimits.ClampCalculationError(null).ShouldBeNull();
    }

    [Fact]
    public void ClampCalculationError_LongText_IsCutToTheColumnWidthWithAMarker()
    {
        var text = new string('x', 5000);

        var clamped = PayslipFieldLimits.ClampCalculationError(text)!;

        clamped.Length.ShouldBe(PayslipFieldLimits.CalculationErrorMaxLength);
        clamped.ShouldEndWith("...");
    }

    [Fact]
    public void ClampCalculationError_DoesNotSplitASurrogatePair()
    {
        // 恰好让截断点落在一个 emoji（代理对）中间
        var text = new string('x', PayslipFieldLimits.CalculationErrorMaxLength - 4) + "\U0001F600" + new string('y', 50);

        var clamped = PayslipFieldLimits.ClampCalculationError(text)!;

        clamped.Length.ShouldBeLessThanOrEqualTo(PayslipFieldLimits.CalculationErrorMaxLength);
        foreach (var c in clamped)
            char.IsSurrogate(c).ShouldBeFalse("a lone surrogate would make PostgreSQL reject the whole insert");
    }
}
