namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// End-to-end smoke tests against a real, locally installed <c>claude</c>.
/// </summary>
/// <remarks>
/// Skipped unless <c>TNZI_RUN_CLI_AGENT_SMOKE=1</c> and the CLI is present, because
/// running them spends the machine owner's account quota. See <see cref="CliSmokeGate"/>.
/// <para>
/// These exist for exactly one reason: the recorded fixtures in
/// <c>StreamJsonAdapterTests</c> keep passing after the real CLI changes its output.
/// The three claims below were established by measurement against a real process and
/// are the ones that break silently when the upstream tool moves.
/// </para>
/// </remarks>
public class ClaudeStreamJsonSmokeTests
{
    private const string ProviderKey = "claude";

    /// <summary>
    /// A turn must finish on the <c>result</c> event, not on stdout EOF.
    /// </summary>
    /// <remarks>
    /// stdin stays open so the protocol can answer <c>control_request</c> mid-turn,
    /// which means the CLI never closes stdout on its own. Waiting for EOF measured
    /// at a full 180s hang for a one-word reply; terminating on <c>result</c> brought
    /// the same turn to 6.4s. The bound here is deliberately generous (60s) - this
    /// test is meant to catch "it hangs until the watchdog", not to police latency.
    /// </remarks>
    [SmokeFact(ProviderKey)]
    public async Task SingleTurn_TerminatesOnResultEvent_NotOnEof()
    {
        var (events, outcome, _, elapsed) = await RunTurnAsync("Reply with exactly: OK");

        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(60));
        outcome.ExitCode.ShouldNotBeNull();
        events.ShouldNotBeEmpty();

        // The reply itself is the model's business and we do not assert its wording.
        // What must hold is that our adapter turned the stream into normalised text.
        events.Where(e => e.Type == CliAgentEventType.Text)
            .Select(e => e.Content)
            .ShouldNotBeEmpty();
    }

    /// <summary>
    /// Every event the real CLI emits must map onto a known event type.
    /// </summary>
    /// <remarks>
    /// The adapter downgrades anything unrecognised to <see cref="CliAgentEventType.Log"/>
    /// rather than throwing, which is the right runtime behaviour but also means protocol
    /// drift is invisible in production. This is the test that makes it visible: a burst
    /// of Log events carrying raw JSON means the upstream format moved.
    /// </remarks>
    [SmokeFact(ProviderKey)]
    public async Task RealOutput_ProducesNoRawJsonFallbackEvents()
    {
        var (events, _, _, _) = await RunTurnAsync("Reply with exactly: OK");

        var rawJsonLogs = events
            .Where(e => e.Type == CliAgentEventType.Log)
            .Where(e => e.Content?.TrimStart().StartsWith('{') == true)
            .ToList();

        rawJsonLogs.ShouldBeEmpty(
            "an unrecognised frame was downgraded to Log, which means the stream-json " +
            "format has moved and the adapter needs updating");
    }

    /// <summary>
    /// A rejected <c>--resume</c> must be classified as
    /// <see cref="CliRunFailureReason.ResumeRejected"/>.
    /// </summary>
    /// <remarks>
    /// This is the most fragile classification in the module and the reason this file
    /// exists. The judgement is made from <b>a stderr phrase plus exit code 1</b>, not
    /// from <c>result.subtype</c> - measurement showed the subtype is the generic
    /// <c>error_during_execution</c>, so it cannot distinguish "this session id is gone"
    /// from "the model errored". A phrase match is inherently brittle across CLI
    /// versions, and nothing but a real run can tell us it has drifted.
    /// <para>
    /// A drifted phrase degrades quietly: the run is still reported as failed, just
    /// under <c>Unknown</c>, so the retry-with-a-fresh-session path stops firing and
    /// every resumed conversation starts silently losing its history.
    /// </para>
    /// </remarks>
    [SmokeFact(ProviderKey)]
    public async Task RejectedResume_IsClassifiedAsResumeRejected()
    {
        // A well-formed but certainly unknown session id: the CLI must reject it.
        var (_, outcome, result, _) = await RunTurnAsync(
            "Reply with exactly: OK",
            resumeSessionId: "00000000-0000-4000-8000-000000000000");

        result.FailureReason.ShouldBe(
            CliRunFailureReason.ResumeRejected,
            $"exit={outcome.ExitCode}, stderr tail was: {outcome.StderrTail}");
    }

    /// <summary>Drive one real turn end to end and hand back what it produced.</summary>
    /// <remarks>
    /// The adapter is stateful and one-shot, so <c>GetResult</c> is called here on the
    /// very instance that drove the session. Calling it on a fresh instance compiles and
    /// returns a plausible-looking result built from nothing but the process outcome -
    /// which is how the first version of this test managed to report a classification
    /// failure that the production path does not have.
    /// </remarks>
    private static async Task<(List<CliAgentEvent> Events, CliSessionOutcome Outcome, CliAgentResult Result, TimeSpan Elapsed)>
        RunTurnAsync(string prompt, string? resumeSessionId = null)
    {
        var provider = CliBuiltInProviders.All[ProviderKey];
        var executablePath = CliSmokeGate.ResolveExecutable(ProviderKey)!;

        // A throwaway working directory: the CLI is free to write into it, and an
        // isolated one keeps the smoke test from touching this repository.
        var workDirectory = Directory.CreateTempSubdirectory("tnzi-cli-smoke-").FullName;

        try
        {
            var context = new CliAgentLaunchContext
            {
                Provider = provider,
                ExecutablePath = executablePath,
                Prompt = prompt,
                WorkingDirectory = workDirectory,
                ResumeSessionId = resumeSessionId,
                ResumeExpected = resumeSessionId is not null
            };

            var adapter = new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance);
            var host = new LocalProcessHost(NullLogger<LocalProcessHost>.Instance);

            // A hard ceiling so a hung CLI fails the test instead of the whole run.
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

            var events = new List<CliAgentEvent>();
            var stopwatch = Stopwatch.StartNew();

            await using var process = await host.StartAsync(adapter.BuildProcess(context), cts.Token);
            try
            {
                await foreach (var evt in adapter.RunAsync(process.Transport, context, cts.Token))
                {
                    events.Add(evt);
                }
            }
            catch (OperationCanceledException)
            {
                // Leave it to the assertions: an empty event list plus the elapsed
                // ceiling says more than an exception from inside the helper would.
            }
            finally
            {
                stopwatch.Stop();
                await process.TerminateAsync(CancellationToken.None);
            }

            var outcome = new CliSessionOutcome
            {
                ExitCode = process.ExitCode,
                StderrTail = process.Transport.StderrTail,
                Elapsed = stopwatch.Elapsed
            };

            return (events, outcome, adapter.GetResult(outcome), stopwatch.Elapsed);
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a smoke test over.
            }
        }
    }
}
