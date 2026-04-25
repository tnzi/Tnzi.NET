namespace Tnzi.AI.Tests.Helpers;

public class AgentRunStatusResolverTests
{
    [Theory]
    [InlineData(FinishReasons.AwaitingApproval, AgentRunStatus.AwaitingApproval)]
    [InlineData(FinishReasons.RequiresClarification, AgentRunStatus.RequiresClarification)]
    [InlineData(FinishReasons.Cancelled, AgentRunStatus.Cancelled)]
    [InlineData(FinishReasons.Error, AgentRunStatus.Failed)]
    [InlineData(FinishReasons.Failed, AgentRunStatus.Failed)]
    [InlineData(FinishReasons.GuardrailRejected, AgentRunStatus.Failed)]
    [InlineData(FinishReasons.QuotaExceeded, AgentRunStatus.Failed)]
    [InlineData(FinishReasons.Rejected, AgentRunStatus.Failed)]
    [InlineData(FinishReasons.MaxHandoffs, AgentRunStatus.Failed)]
    public void Resolve_KnownFailureReason_MapsToExpectedStatus(string finishReason, AgentRunStatus expected)
    {
        AgentRunStatusResolver.Resolve(finishReason).ShouldBe(expected);
    }

    [Theory]
    [InlineData(FinishReasons.Stop)]
    [InlineData(FinishReasons.Completed)]
    [InlineData(FinishReasons.MaxToolIterations)]
    [InlineData(FinishReasons.AgentAsToolsComplete)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown-reason")]
    public void Resolve_NonFailureReason_ReturnsDefaultStatus(string? finishReason)
    {
        AgentRunStatusResolver.Resolve(finishReason).ShouldBe(AgentRunStatus.Completed);
    }

    [Fact]
    public void Resolve_CustomDefaultStatus_Honored()
    {
        AgentRunStatusResolver.Resolve(null, AgentRunStatus.Running).ShouldBe(AgentRunStatus.Running);
    }

    [Fact]
    public void EnsureStatus_StatusAlreadySet_ReturnsUnchanged()
    {
        var result = new AgentRunResult
        {
            Response = "done",
            Status = AgentRunStatus.Cancelled,
            FinishReason = FinishReasons.Failed // would normally map to Failed, but Status wins
        };

        var fixedResult = AgentRunStatusResolver.EnsureStatus(result);

        fixedResult.Status.ShouldBe(AgentRunStatus.Cancelled);
    }

    [Fact]
    public void EnsureStatus_StatusMissing_AssignsResolvedStatus()
    {
        var result = new AgentRunResult
        {
            Response = "blocked",
            FinishReason = FinishReasons.GuardrailRejected
        };

        var fixedResult = AgentRunStatusResolver.EnsureStatus(result);

        fixedResult.Status.ShouldBe(AgentRunStatus.Failed);
    }

    [Fact]
    public void EnsureStatus_StatusMissingNoFinishReason_UsesDefaultStatus()
    {
        var result = new AgentRunResult { Response = "done" };

        var fixedResult = AgentRunStatusResolver.EnsureStatus(result);

        fixedResult.Status.ShouldBe(AgentRunStatus.Completed);
    }

    [Theory]
    [InlineData(FinishReasons.GuardrailRejected, false)]
    [InlineData(FinishReasons.QuotaExceeded, false)]
    [InlineData(FinishReasons.Error, false)]
    [InlineData(FinishReasons.Failed, false)]
    [InlineData(FinishReasons.Rejected, false)]
    [InlineData(FinishReasons.MaxHandoffs, false)]
    [InlineData(FinishReasons.MaxToolIterations, false)]
    [InlineData(FinishReasons.Stop, true)]
    [InlineData(FinishReasons.Completed, true)]
    [InlineData(FinishReasons.AwaitingApproval, true)]
    [InlineData(null, true)]
    public void ShouldGenerateThreadTitle_ReturnsExpected(string? finishReason, bool expected)
    {
        AgentRunStatusResolver.ShouldGenerateThreadTitle(finishReason).ShouldBe(expected);
    }
}
