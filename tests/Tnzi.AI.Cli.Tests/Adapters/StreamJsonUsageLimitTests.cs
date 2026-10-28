namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// The account's usage limit, as stream-json reports it, ends a run with its own failure reason and the reset time.
/// </summary>
/// <remarks>
/// The frames follow the message schema bundled in claude 2.1.303: the refusal is an assistant message carrying
/// <c>error: "rate_limit"</c> with <c>api_error: "usage_limit_reached"</c> and <c>api_error_params.rate_limit_info</c>
/// (<c>status</c>, <c>resetsAt</c> in Unix seconds, <c>rateLimitType</c>); <c>rate_limit_event</c> carries the same
/// <c>rate_limit_info</c> shape. A real refusal cannot be produced on demand, so these are the frames the schema describes.
/// </remarks>
public class StreamJsonUsageLimitTests
{
    private const long ResetsAtSeconds = 1_760_886_000;
    private static readonly DateTimeOffset ResetsAt = DateTimeOffset.FromUnixTimeSeconds(ResetsAtSeconds);

    private const string Notice = "You've hit your limit · resets 3pm (UTC)";

    private static readonly string RefusalFrame =
        $$$"""{"type":"assistant","uuid":"a-1","session_id":"s1","error":"rate_limit","is_api_error_message":true,"api_error_status":429,"api_error":"usage_limit_reached","api_error_params":{"rate_limit_info":{"status":"rejected","resetsAt":{{{ResetsAtSeconds}}},"rateLimitType":"five_hour"}},"message":{"role":"assistant","model":"<synthetic>","content":[{"type":"text","text":"{{{Notice}}}"}]}}""";

    private static readonly string ErrorResultFrame =
        $$$"""{"type":"result","subtype":"success","is_error":true,"result":"{{{Notice}}}","num_turns":1,"session_id":"s1"}""";

    private static CliAgentLaunchContext Context(string? resume = null) => new()
    {
        Provider = CliBuiltInProviders.All["claude"],
        ExecutablePath = "/nonexistent/claude",
        Prompt = "hello",
        WorkingDirectory = "/tmp/does-not-need-to-exist",
        ResumeSessionId = resume
    };

    private static async Task<(List<CliAgentEvent> Events, StreamJsonAdapter Adapter)> RunAsync(
        IReadOnlyList<string> frames, string? resume = null, string stderr = "")
    {
        var adapter = new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance);
        var events = new List<CliAgentEvent>();
        await foreach (var evt in adapter.RunAsync(new FakeCliAgentTransport(frames, stderrTail: stderr), Context(resume), CancellationToken.None))
        {
            events.Add(evt);
        }

        return (events, adapter);
    }

    [Fact]
    public async Task TheRefusal_EndsTheRunAsUsageLimit_WithTheResetTime_AndIsNotPartOfTheAnswer()
    {
        var (events, adapter) = await RunAsync([RefusalFrame, ErrorResultFrame]);

        events.ShouldNotContain(e => e.Type == CliAgentEventType.Text, "the notice is the CLI's, not the model's answer");
        var status = events.ShouldHaveSingleItem();
        status.Type.ShouldBe(CliAgentEventType.Status);
        status.Status.ShouldBe("usage_limit");
        status.Content.ShouldBe(Notice);

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 1 });
        result.Status.ShouldBe(CliRunStatus.Failed);
        result.FailureReason.ShouldBe(CliRunFailureReason.UsageLimit);
        result.UsageLimitResetsAt.ShouldBe(ResetsAt);
        result.Error.ShouldNotBeNull().ShouldContain("You've hit your limit");
        result.Output.ShouldBeNull();
    }

    [Fact]
    public async Task OnAResumedTurn_TheRefusalIsNotMistakenForARejectedResume()
    {
        // A refused turn runs no model turn, costs nothing and exits non-zero with something on stderr: exactly the
        // corroborating evidence the resume-rejection fallback looks for. Classified that way, the session pointer would be
        // dropped and the turn retried with a fresh session that is refused again.
        var (_, adapter) = await RunAsync([RefusalFrame, ErrorResultFrame], resume: "s0", stderr: "error");

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 1, StderrTail = "error" });

        result.FailureReason.ShouldBe(CliRunFailureReason.UsageLimit);
        result.ResumeRejected.ShouldBeFalse();
        result.SessionId.ShouldBe("s1");
    }

    [Fact]
    public async Task ARejectedRateLimitEvent_FollowedByAnErrorResult_IsAUsageLimitWithTheEventsResetTime()
    {
        var (events, adapter) = await RunAsync(
        [
            $$"""{"type":"rate_limit_event","rate_limit_info":{"status":"rejected","resetsAt":{{ResetsAtSeconds}},"rateLimitType":"seven_day"},"uuid":"r-1","session_id":"s1"}""",
            ErrorResultFrame
        ]);

        events.ShouldHaveSingleItem().Content.ShouldBe("rejected");
        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 1 });
        result.FailureReason.ShouldBe(CliRunFailureReason.UsageLimit);
        result.UsageLimitResetsAt.ShouldBe(ResetsAt);
        adapter.LastRateLimitInfo.ShouldNotBeNull().RateLimitType.ShouldBe("seven_day");
    }

    [Fact]
    public async Task AnAllowedRateLimitEvent_IsReportedButChangesNothing()
    {
        var (events, adapter) = await RunAsync(
        [
            $$"""{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","resetsAt":{{ResetsAtSeconds}},"rateLimitType":"five_hour"},"uuid":"r-1","session_id":"s1"}""",
            """{"type":"assistant","uuid":"a-1","session_id":"s1","message":{"role":"assistant","model":"m","content":[{"type":"text","text":"ok"}]}}""",
            """{"type":"result","subtype":"success","is_error":false,"result":"ok","num_turns":1,"session_id":"s1"}"""
        ]);

        events.First().Status.ShouldBe("rate_limit");
        events.First().Content.ShouldBe("allowed");
        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Status.ShouldBe(CliRunStatus.Completed);
        result.UsageLimitResetsAt.ShouldBeNull();
        adapter.LastRateLimitInfo.ShouldNotBeNull().ResetsAt.ShouldBe(ResetsAt);
    }

    [Fact]
    public async Task ARateLimitWithoutTheAccountsLimitInfo_IsRateLimited_WithNoResetTime()
    {
        var (_, adapter) = await RunAsync(
        [
            """{"type":"assistant","uuid":"a-1","session_id":"s1","error":"rate_limit","message":{"role":"assistant","model":"<synthetic>","content":[{"type":"text","text":"API Error: Rate limited"}]}}""",
            """{"type":"result","subtype":"success","is_error":true,"result":"API Error: Rate limited","num_turns":1,"session_id":"s1"}"""
        ]);

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 1 });
        result.FailureReason.ShouldBe(CliRunFailureReason.RateLimited);
        result.UsageLimitResetsAt.ShouldBeNull();
    }

    [Fact]
    public async Task ModelTextAboutLimits_IsJustText()
    {
        var (events, adapter) = await RunAsync(
        [
            """{"type":"assistant","uuid":"a-1","session_id":"s1","message":{"role":"assistant","model":"m","content":[{"type":"text","text":"You've hit your limit of three guesses."}]}}""",
            """{"type":"result","subtype":"success","is_error":false,"result":"You've hit your limit of three guesses.","num_turns":1,"session_id":"s1"}"""
        ]);

        events.ShouldHaveSingleItem().Type.ShouldBe(CliAgentEventType.Text);
        adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Status.ShouldBe(CliRunStatus.Completed);
    }

    [Fact]
    public async Task AnErrorFieldOfAnotherShape_DoesNotCostTheFrame()
    {
        // The error fields are read leniently: a frame where "error" is an object must still be the terminal result
        var (_, adapter) = await RunAsync(
            ["""{"type":"result","subtype":"success","is_error":false,"result":"ok","num_turns":1,"session_id":"s1","error":{"kind":"x"}}"""]);

        adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Status.ShouldBe(CliRunStatus.Completed);
    }
}
