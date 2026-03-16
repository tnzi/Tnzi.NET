namespace Tnzi.AI.Tests.Options;

/// <summary>
/// ReasoningEffort 枚举和 ThinkingOptions 配置测试
/// </summary>
public class ThinkingOptionsTests
{
    #region ReasoningEffort Enum

    [Fact]
    public void ReasoningEffort_None_HasValue0()
    {
        ((int)ReasoningEffort.None).ShouldBe(0);
    }

    [Fact]
    public void ReasoningEffort_Low_HasValue1()
    {
        ((int)ReasoningEffort.Low).ShouldBe(1);
    }

    [Fact]
    public void ReasoningEffort_Medium_HasValue2()
    {
        ((int)ReasoningEffort.Medium).ShouldBe(2);
    }

    [Fact]
    public void ReasoningEffort_High_HasValue3()
    {
        ((int)ReasoningEffort.High).ShouldBe(3);
    }

    [Fact]
    public void ReasoningEffort_Max_HasValue4()
    {
        ((int)ReasoningEffort.Max).ShouldBe(4);
    }

    [Fact]
    public void ReasoningEffort_ValuesAreOrdered()
    {
        ((int)ReasoningEffort.None).ShouldBeLessThan((int)ReasoningEffort.Low);
        ((int)ReasoningEffort.Low).ShouldBeLessThan((int)ReasoningEffort.Medium);
        ((int)ReasoningEffort.Medium).ShouldBeLessThan((int)ReasoningEffort.High);
        ((int)ReasoningEffort.High).ShouldBeLessThan((int)ReasoningEffort.Max);
    }

    #endregion

    #region ThinkingOptions Defaults

    [Fact]
    public void ThinkingOptions_DefaultEffort_IsNone()
    {
        var options = new ThinkingOptions();
        options.Effort.ShouldBe(ReasoningEffort.None);
    }

    [Fact]
    public void ThinkingOptions_DefaultBudgetTokens_IsNull()
    {
        var options = new ThinkingOptions();
        options.BudgetTokens.ShouldBeNull();
    }

    #endregion

    #region ThinkingOptions Configuration

    [Fact]
    public void ThinkingOptions_SetEffort_RetainValue()
    {
        var options = new ThinkingOptions { Effort = ReasoningEffort.High };
        options.Effort.ShouldBe(ReasoningEffort.High);
    }

    [Fact]
    public void ThinkingOptions_SetBudgetTokens_RetainValue()
    {
        var options = new ThinkingOptions { BudgetTokens = 8192 };
        options.BudgetTokens.ShouldBe(8192);
    }

    [Fact]
    public void ThinkingOptions_SetAllProperties_RetainValues()
    {
        var options = new ThinkingOptions
        {
            Effort = ReasoningEffort.Max,
            BudgetTokens = 32768
        };

        options.Effort.ShouldBe(ReasoningEffort.Max);
        options.BudgetTokens.ShouldBe(32768);
    }

    #endregion
}
