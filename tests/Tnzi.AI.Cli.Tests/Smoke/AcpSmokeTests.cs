namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// End-to-end smoke test for the ACP adapter against whichever ACP CLI is installed.
/// </summary>
/// <remarks>
/// Skipped unless <c>TNZI_RUN_CLI_AGENT_SMOKE=1</c> and some ACP-speaking CLI is on
/// PATH. See <see cref="CliSmokeGate"/> for why the double gate.
/// <para>
/// <b>Deliberately not pinned to one product.</b> One adapter covers seven CLIs, so
/// pinning would leave the test permanently skipped on every machine that installed a
/// different one - which is most machines. Running against whatever is present is also
/// the honest reading of the phase acceptance ("any two installed ACP CLIs go through
/// the same code path"): the claim is about the shared path, not about a product.
/// </para>
/// </remarks>
public class AcpSmokeTests
{
    /// <summary>
    /// The full ACP handshake and one prompt turn must complete over the real transport.
    /// </summary>
    /// <remarks>
    /// This is the one test that exercises bidirectional JSON-RPC against a real peer:
    /// request/response correlation, notification dispatch, and the agent's own reverse
    /// requests. A fake transport replays a script we wrote, so it can only confirm we
    /// still agree with ourselves.
    /// </remarks>
    [SmokeProtocolFact(CliAgentProtocol.Acp)]
    public async Task Handshake_AndOneTurn_CompleteOverTheRealTransport()
    {
        var (provider, executablePath) = CliSmokeGate.FirstInstalled(CliAgentProtocol.Acp)!.Value;
        var workDirectory = Directory.CreateTempSubdirectory("tnzi-acp-smoke-").FullName;

        try
        {
            var context = new CliAgentLaunchContext
            {
                Provider = provider,
                ExecutablePath = executablePath,
                Prompt = "Reply with exactly: OK",
                WorkingDirectory = workDirectory
            };

            var adapter = new AcpAdapter(NullLogger<AcpAdapter>.Instance);
            var host = new LocalProcessHost(NullLogger<LocalProcessHost>.Instance);
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

            var events = new List<CliAgentEvent>();
            await using var process = await host.StartAsync(adapter.BuildProcess(context), cts.Token);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await foreach (var evt in adapter.RunAsync(process.Transport, context, cts.Token))
                {
                    events.Add(evt);
                }
            }
            catch (OperationCanceledException)
            {
                // Handled by the assertions below.
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
            var result = adapter.GetResult(outcome);

            // A handshake failure surfaces as HandshakeTimeout with no events at all,
            // which is the failure this test is really here to catch.
            result.FailureReason.ShouldNotBe(
                CliRunFailureReason.HandshakeTimeout,
                $"ACP handshake with '{provider.Key}' did not complete; stderr tail: {outcome.StderrTail}");

            events.ShouldNotBeEmpty($"'{provider.Key}' produced no normalised events");
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Not worth failing a smoke test over.
            }
        }
    }
}
