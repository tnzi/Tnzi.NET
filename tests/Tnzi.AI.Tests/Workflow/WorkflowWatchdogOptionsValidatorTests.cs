using Tnzi.AI.Workflow.Options;

namespace Tnzi.AI.Tests.Workflow;

public class WorkflowWatchdogOptionsValidatorTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        Validate(new WorkflowWatchdogOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void HeartbeatInterval_BelowOneSecond_Fails()
    {
        var result = Validate(new WorkflowWatchdogOptions { HeartbeatInterval = TimeSpan.FromMilliseconds(500) });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains("HeartbeatInterval must be at least 1 second", StringComparison.Ordinal));
    }

    /// <summary>
    /// The error is attributed to <c>RunningTimeout</c>, the only one of the pair that is a
    /// runtime setting: an admin lowering it in the settings center must be told the floor
    /// for the field on the page, not about a field they cannot edit there.
    /// </summary>
    [Fact]
    public void HeartbeatInterval_SparserThanHalfTheRunningTimeout_Fails_NamingRunningTimeout()
    {
        // A heartbeat that can miss once and still be older than the cutoff protects nothing.
        var result = Validate(new WorkflowWatchdogOptions
        {
            RunningTimeout = TimeSpan.FromMinutes(10),
            HeartbeatInterval = TimeSpan.FromMinutes(6)
        });

        result.Failed.ShouldBeTrue();
        var failure = result.Failures.ShouldHaveSingleItem();
        failure.ShouldContain("RunningTimeout: RunningTimeout (00:10:00)");
        failure.ShouldNotContain("HeartbeatInterval:");
        failure.ShouldContain("at least 2 x HeartbeatInterval (00:06:00), i.e. 00:12:00");
    }

    [Fact]
    public void HeartbeatInterval_AtHalfTheRunningTimeout_IsAccepted()
    {
        Validate(new WorkflowWatchdogOptions
        {
            RunningTimeout = TimeSpan.FromMinutes(10),
            HeartbeatInterval = TimeSpan.FromMinutes(5)
        }).Succeeded.ShouldBeTrue();
    }

    private static ValidateOptionsResult Validate(WorkflowWatchdogOptions options)
        => new WorkflowWatchdogOptionsValidator().Validate(null, options);
}
