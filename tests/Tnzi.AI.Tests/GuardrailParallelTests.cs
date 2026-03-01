using Tnzi.Exceptions;

namespace Tnzi.AI.Tests;

/// <summary>
/// Guardrail 并行执行和 Tripwire 机制的单元测试
/// </summary>
public class GuardrailParallelTests
{
    private static IOptions<AIOptions> CreateOptions(Action<GuardrailsOptions>? configure = null)
    {
        var options = new AIOptions();
        options.Guardrails.Enabled = true;
        configure?.Invoke(options.Guardrails);
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    #region Sequential Mode (向后兼容验证)

    [Fact]
    public async Task RunInputGuardrails_SequentialMode_StopsOnFirstRejection()
    {
        // Arrange: 第一个 guardrail 拒绝，第二个记录是否被调用
        var tracker = new CallTracker();
        var options = CreateOptions(g => g.ExecutionMode = GuardrailExecutionMode.Sequential);

        var runner = new GuardrailRunner(
            [new RejectingGuardrail("First"), new TrackingGuardrail(tracker)],
            [],
            options,
            Mock.Of<ILogger<GuardrailRunner>>());

        // Act
        var (_, rejection) = await runner.RunInputGuardrailsAsync("test input");

        // Assert: 第一个拒绝后第二个不应被调用
        rejection.ShouldNotBeNull();
        rejection.GuardrailName.ShouldBe("First");
        tracker.WasCalled.ShouldBeFalse();
    }

    [Fact]
    public void DefaultExecutionMode_IsSequential()
    {
        // 验证向后兼容性：默认执行模式为顺序
        var options = new GuardrailsOptions();
        options.ExecutionMode.ShouldBe(GuardrailExecutionMode.Sequential);
    }

    #endregion

    #region Parallel Mode — Input Guardrails

    [Fact]
    public async Task RunInputGuardrails_ParallelMode_AllGuardrailsExecute()
    {
        // Arrange: 两个追踪 guardrail，验证并行模式下所有 guardrail 都被调用
        var tracker1 = new CallTracker();
        var tracker2 = new CallTracker();
        var options = CreateOptions(g => g.ExecutionMode = GuardrailExecutionMode.Parallel);

        var runner = new GuardrailRunner(
            [new TrackingGuardrail(tracker1), new TrackingGuardrail(tracker2)],
            [],
            options,
            Mock.Of<ILogger<GuardrailRunner>>());

        // Act
        var (text, rejection) = await runner.RunInputGuardrailsAsync("test input");

        // Assert: 两个 guardrail 都被调用，且没有拒绝
        rejection.ShouldBeNull();
        text.ShouldBe("test input");
        tracker1.WasCalled.ShouldBeTrue();
        tracker2.WasCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task RunInputGuardrails_ParallelMode_ReturnsFirstRejection()
    {
        // Arrange: 两个 guardrail 都拒绝，应返回注册顺序中第一个的拒绝结果
        var options = CreateOptions(g => g.ExecutionMode = GuardrailExecutionMode.Parallel);

        var runner = new GuardrailRunner(
            [new SlowRejectingGuardrail("First", delayMs: 50), new RejectingGuardrail("Second")],
            [],
            options,
            Mock.Of<ILogger<GuardrailRunner>>());

        // Act
        var (_, rejection) = await runner.RunInputGuardrailsAsync("test input");

        // Assert: 按注册顺序返回第一个拒绝
        rejection.ShouldNotBeNull();
        rejection.GuardrailName.ShouldBe("First");
    }

    [Fact]
    public async Task RunInputGuardrails_ParallelMode_TripwireCancelsOthers()
    {
        // Arrange: tripwire guardrail 立即触发，慢 guardrail 应被取消
        var slowTracker = new CallTracker();
        var options = CreateOptions(g => g.ExecutionMode = GuardrailExecutionMode.Parallel);

        var runner = new GuardrailRunner(
            [new SlowTrackingGuardrail(slowTracker, delayMs: 500), new TripwireInputGuardrail("TripwireGuard", "Dangerous content detected")],
            [],
            options,
            Mock.Of<ILogger<GuardrailRunner>>());

        // Act
        var (_, rejection) = await runner.RunInputGuardrailsAsync("dangerous input");

        // Assert: tripwire 触发，返回 tripwire 拒绝结果
        rejection.ShouldNotBeNull();
        rejection.GuardrailName.ShouldBe("TripwireGuard");
        rejection.Reason.ShouldBe("Dangerous content detected");
        // 慢 guardrail 可能已开始但应被取消（不会完成标记）
        slowTracker.WasCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task RunInputGuardrails_ParallelMode_TripwireTakesPriorityOverRejection()
    {
        // Arrange: 一个普通拒绝 + 一个 tripwire，tripwire 优先级更高
        var options = CreateOptions(g => g.ExecutionMode = GuardrailExecutionMode.Parallel);

        var runner = new GuardrailRunner(
            [new RejectingGuardrail("NormalReject"), new TripwireInputGuardrail("TripwireGuard", "Critical violation")],
            [],
            options,
            Mock.Of<ILogger<GuardrailRunner>>());

        // Act
        var (_, rejection) = await runner.RunInputGuardrailsAsync("test input");

        // Assert: tripwire 结果优先
        rejection.ShouldNotBeNull();
        rejection.GuardrailName.ShouldBe("TripwireGuard");
    }

    [Fact]
    public async Task RunInputGuardrails_ParallelMode_ModificationsAppliedInOrder()
    {
        // Arrange: 两个修改 guardrail，验证按注册顺序应用
        var options = CreateOptions(g => g.ExecutionMode = GuardrailExecutionMode.Parallel);

        var runner = new GuardrailRunner(
            [new ModifyingGuardrail("First", "modified-by-first"), new ModifyingGuardrail("Second", "modified-by-second")],
            [],
            options,
            Mock.Of<ILogger<GuardrailRunner>>());

        // Act
        var (text, rejection) = await runner.RunInputGuardrailsAsync("original");

        // Assert: 最后一个修改生效（按注册顺序依次覆盖）
        rejection.ShouldBeNull();
        text.ShouldBe("modified-by-second");
    }

    #endregion

    #region Parallel Mode — Output Guardrails

    [Fact]
    public async Task RunOutputGuardrails_ParallelMode_Works()
    {
        // Arrange: 并行模式下输出 guardrail 正常工作
        var tracker1 = new CallTracker();
        var tracker2 = new CallTracker();
        var options = CreateOptions(g => g.ExecutionMode = GuardrailExecutionMode.Parallel);

        var runner = new GuardrailRunner(
            [],
            [new TrackingOutputGuardrail(tracker1), new TrackingOutputGuardrail(tracker2)],
            options,
            Mock.Of<ILogger<GuardrailRunner>>());

        // Act
        var (text, rejection) = await runner.RunOutputGuardrailsAsync("output text");

        // Assert
        rejection.ShouldBeNull();
        text.ShouldBe("output text");
        tracker1.WasCalled.ShouldBeTrue();
        tracker2.WasCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task RunOutputGuardrails_ParallelMode_TripwireWorks()
    {
        // Arrange: 输出 tripwire guardrail
        var options = CreateOptions(g => g.ExecutionMode = GuardrailExecutionMode.Parallel);

        var runner = new GuardrailRunner(
            [],
            [new TripwireOutputGuardrail("OutputTripwire", "Sensitive data detected")],
            options,
            Mock.Of<ILogger<GuardrailRunner>>());

        // Act
        var (_, rejection) = await runner.RunOutputGuardrailsAsync("output with sensitive data");

        // Assert
        rejection.ShouldNotBeNull();
        rejection.GuardrailName.ShouldBe("OutputTripwire");
        rejection.Reason.ShouldBe("Sensitive data detected");
    }

    #endregion

    #region TripwireGuardrailException

    [Fact]
    public void TripwireGuardrailException_PropertiesAreSet()
    {
        var ex = new TripwireGuardrailException("TestGuardrail", "Test reason");

        ex.GuardrailName.ShouldBe("TestGuardrail");
        ex.Message.ShouldBe("Test reason");
        ex.Code.ShouldBe("AI_GUARDRAIL_TRIPWIRE");
    }

    [Fact]
    public void TripwireGuardrailException_NullGuardrailName_Throws()
    {
        Should.Throw<ArgumentException>(() => new TripwireGuardrailException(null!, "reason"));
    }

    [Fact]
    public void TripwireGuardrailException_EmptyGuardrailName_Throws()
    {
        Should.Throw<ArgumentException>(() => new TripwireGuardrailException("", "reason"));
    }

    #endregion

    #region Disabled Guardrails — Parallel Mode Bypass

    [Fact]
    public async Task RunInputGuardrails_Disabled_BypassesParallelMode()
    {
        var tracker = new CallTracker();
        var options = CreateOptions(g =>
        {
            g.Enabled = false;
            g.ExecutionMode = GuardrailExecutionMode.Parallel;
        });

        var runner = new GuardrailRunner(
            [new TrackingGuardrail(tracker)],
            [],
            options,
            Mock.Of<ILogger<GuardrailRunner>>());

        var (text, rejection) = await runner.RunInputGuardrailsAsync("test input");

        rejection.ShouldBeNull();
        text.ShouldBe("test input");
        tracker.WasCalled.ShouldBeFalse();
    }

    #endregion

    #region Mock Guardrails

    /// <summary>
    /// 调用追踪器
    /// </summary>
    private class CallTracker
    {
        private int _called;
        private int _completed;

        public bool WasCalled => Interlocked.CompareExchange(ref _called, 0, 0) > 0;
        public bool WasCompleted => Interlocked.CompareExchange(ref _completed, 0, 0) > 0;

        public void MarkCalled() => Interlocked.Increment(ref _called);
        public void MarkCompleted() => Interlocked.Increment(ref _completed);
    }

    /// <summary>
    /// 追踪是否被调用的输入 Guardrail（始终通过）
    /// </summary>
    private class TrackingGuardrail(CallTracker tracker) : IInputGuardrail
    {
        public Task<GuardrailResult> ValidateAsync(string input, CancellationToken ct = default)
        {
            tracker.MarkCalled();
            tracker.MarkCompleted();
            return Task.FromResult(GuardrailResult.Allowed());
        }
    }

    /// <summary>
    /// 追踪是否被调用的输出 Guardrail（始终通过）
    /// </summary>
    private class TrackingOutputGuardrail(CallTracker tracker) : IOutputGuardrail
    {
        public Task<GuardrailResult> ValidateAsync(string output, CancellationToken ct = default)
        {
            tracker.MarkCalled();
            tracker.MarkCompleted();
            return Task.FromResult(GuardrailResult.Allowed());
        }
    }

    /// <summary>
    /// 始终拒绝的输入 Guardrail
    /// </summary>
    private class RejectingGuardrail(string name) : IInputGuardrail
    {
        public Task<GuardrailResult> ValidateAsync(string input, CancellationToken ct = default)
        {
            return Task.FromResult(GuardrailResult.Rejected(name, $"Rejected by {name}"));
        }
    }

    /// <summary>
    /// 慢速拒绝的输入 Guardrail（用于验证并行执行时的排序）
    /// </summary>
    private class SlowRejectingGuardrail(string name, int delayMs) : IInputGuardrail
    {
        public async Task<GuardrailResult> ValidateAsync(string input, CancellationToken ct = default)
        {
            await Task.Delay(delayMs, ct);
            return GuardrailResult.Rejected(name, $"Rejected by {name}");
        }
    }

    /// <summary>
    /// 慢速追踪 Guardrail（用于验证 tripwire 取消行为）
    /// </summary>
    private class SlowTrackingGuardrail(CallTracker tracker, int delayMs) : IInputGuardrail
    {
        public async Task<GuardrailResult> ValidateAsync(string input, CancellationToken ct = default)
        {
            tracker.MarkCalled();
            await Task.Delay(delayMs, ct);
            tracker.MarkCompleted();
            return GuardrailResult.Allowed();
        }
    }

    /// <summary>
    /// Tripwire 输入 Guardrail — 抛出 TripwireGuardrailException 立即中止
    /// </summary>
    private class TripwireInputGuardrail(string name, string reason) : IInputGuardrail
    {
        public Task<GuardrailResult> ValidateAsync(string input, CancellationToken ct = default)
        {
            throw new TripwireGuardrailException(name, reason);
        }
    }

    /// <summary>
    /// Tripwire 输出 Guardrail — 抛出 TripwireGuardrailException 立即中止
    /// </summary>
    private class TripwireOutputGuardrail(string name, string reason) : IOutputGuardrail
    {
        public Task<GuardrailResult> ValidateAsync(string output, CancellationToken ct = default)
        {
            throw new TripwireGuardrailException(name, reason);
        }
    }

    /// <summary>
    /// 修改内容的输入 Guardrail
    /// </summary>
    private class ModifyingGuardrail(string name, string modifiedContent) : IInputGuardrail
    {
        public Task<GuardrailResult> ValidateAsync(string input, CancellationToken ct = default)
        {
            return Task.FromResult(GuardrailResult.Modified(name, modifiedContent, $"Modified by {name}"));
        }
    }

    #endregion
}
