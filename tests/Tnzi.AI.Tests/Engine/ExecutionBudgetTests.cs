namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// 执行效率监控测试
/// </summary>
public class ExecutionBudgetTests
{
    private ExecutionBudgetTracker CreateTracker(int threshold = 500, int limit = 3)
    {
        return new ExecutionBudgetTracker(new ExecutionBudgetOptions
        {
            LowIncrementThreshold = threshold,
            ConsecutiveLowIncrementLimit = limit,
            WrapUpNudge = "Please wrap up."
        });
    }

    [Fact]
    public void RecordIteration_HighIncrements_NoNudge()
    {
        var tracker = CreateTracker();

        // 每次 1000 tokens 增量 → 高效
        tracker.RecordIteration(1000).ShouldBeNull();
        tracker.RecordIteration(2000).ShouldBeNull();
        tracker.RecordIteration(3000).ShouldBeNull();
        tracker.RecordIteration(4000).ShouldBeNull();

        tracker.NudgeInjected.ShouldBeFalse();
    }

    [Fact]
    public void RecordIteration_ConsecutiveLowIncrements_InjectsNudge()
    {
        var tracker = CreateTracker(threshold: 500, limit: 3);

        // 第一次正常
        tracker.RecordIteration(1000).ShouldBeNull();
        // 后续连续低增量
        tracker.RecordIteration(1100).ShouldBeNull(); // +100 < 500
        tracker.RecordIteration(1200).ShouldBeNull(); // +100 < 500
        var nudge = tracker.RecordIteration(1300);     // +100 < 500, 第3次 → nudge

        nudge.ShouldBe("Please wrap up.");
        tracker.NudgeInjected.ShouldBeTrue();
    }

    [Fact]
    public void RecordIteration_LowThenHigh_ResetsCounter()
    {
        var tracker = CreateTracker(threshold: 500, limit: 3);

        tracker.RecordIteration(1000);
        tracker.RecordIteration(1100); // +100 → low (1)
        tracker.RecordIteration(1200); // +100 → low (2)
        tracker.RecordIteration(2200); // +1000 → high → reset

        tracker.ConsecutiveLowIncrements.ShouldBe(0);
        tracker.NudgeInjected.ShouldBeFalse();
    }

    [Fact]
    public void RecordIteration_NudgeOnlyOnce()
    {
        var tracker = CreateTracker(threshold: 500, limit: 2);

        tracker.RecordIteration(1000); // first: increment = 1000 → high
        tracker.RecordIteration(1100); // +100 → low (1)
        var nudge1 = tracker.RecordIteration(1200); // +100 → low (2) → nudge

        nudge1.ShouldNotBeNull();

        // 后续调用不再返回 nudge
        tracker.RecordIteration(1300).ShouldBeNull();
        tracker.RecordIteration(1400).ShouldBeNull();
    }

    [Fact]
    public void RecordIteration_FirstCall_NoNudge()
    {
        var tracker = CreateTracker();

        // 第一次调用总是 ok（因为 increment = totalTokens - 0 = totalTokens）
        tracker.RecordIteration(100).ShouldBeNull();
    }

    [Fact]
    public void RecordIteration_ZeroTokens_CountsAsLow()
    {
        var tracker = CreateTracker(threshold: 500, limit: 2);

        tracker.RecordIteration(1000);
        tracker.RecordIteration(1000); // +0 → low (1)
        var nudge = tracker.RecordIteration(1000); // +0 → low (2) → nudge

        nudge.ShouldBe("Please wrap up.");
    }
}
