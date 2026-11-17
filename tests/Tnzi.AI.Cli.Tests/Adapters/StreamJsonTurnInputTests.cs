using static Tnzi.AI.Cli.Tests.MidTurnClaudeFrames;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// A user message handed to the stream-json adapter while a turn runs (<see cref="ICliTurnInput"/>): written with its id as
/// the uuid and <c>priority: "next"</c>, reported taken where claude folds it into the turn, and a message that missed the
/// turn's end keeps the reading going for the turn claude gives it.
/// </summary>
public class StreamJsonTurnInputTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static CliAgentLaunchContext Context() => new()
    {
        Provider = CliBuiltInProviders.All["claude"],
        ExecutablePath = "/nonexistent/claude",
        Prompt = "run the tests",
        WorkingDirectory = "/tmp/does-not-need-to-exist"
    };

    /// <summary>Reads the adapter in the background, collecting its events.</summary>
    private sealed class Reading
    {
        public List<CliAgentEvent> Events { get; } = [];

        public required Task Done { get; init; }

        public static Reading Start(StreamJsonAdapter adapter, FakeCliAgentTransport transport)
        {
            var events = new List<CliAgentEvent>();
            var done = Task.Run(async () =>
            {
                await foreach (var evt in adapter.RunAsync(transport, Context(), CancellationToken.None))
                {
                    lock (events)
                    {
                        events.Add(evt);
                    }
                }
            });
            return new Reading(events) { Done = done };
        }

        private Reading(List<CliAgentEvent> events) => Events = events;

        public async Task UntilAsync(Func<IReadOnlyList<CliAgentEvent>, bool> condition)
        {
            using var timeout = new CancellationTokenSource(Timeout);
            while (true)
            {
                lock (Events)
                {
                    if (condition(Events))
                    {
                        return;
                    }
                }

                timeout.Token.ThrowIfCancellationRequested();
                await Task.Delay(10);
            }
        }
    }

    [Fact]
    public async Task AMessageWrittenMidTurn_CarriesItsIdAndPriorityNext_AndIsTakenInWhereClaudeFoldsItIn()
    {
        var adapter = new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance);
        var transport = new FakeCliAgentTransport([], onWrite: line => UuidOf(line) is { } uuid
            ? [Lifecycle(uuid, "queued")]
            : [Init(), ToolUse("msg_1", "call_1", "Bash")]);
        var reading = Reading.Start(adapter, transport);
        await reading.UntilAsync(e => e.Any(x => x.Type == CliAgentEventType.ToolUse));

        var id = Guid.NewGuid();
        (await adapter.SendUserMessageAsync(new CliTurnInputMessage(id, "also run the linter", []), CancellationToken.None)).ShouldBeTrue();

        using (var written = JsonDocument.Parse(transport.Written[^1]))
        {
            written.RootElement.GetProperty("type").GetString().ShouldBe("user");
            written.RootElement.GetProperty("uuid").GetString().ShouldBe(id.ToString());
            written.RootElement.GetProperty("priority").GetString().ShouldBe("next");
            written.RootElement.GetProperty("message").GetProperty("content")[0].GetProperty("text").GetString().ShouldBe("also run the linter");
        }

        transport.Emit(
            ToolResult("call_1", "12 passed"),
            Lifecycle(id.ToString(), "started"),
            Text("msg_2", "Running the linter as well."),
            Lifecycle(id.ToString(), "completed"),
            Result("Running the linter as well.", Guid.NewGuid().ToString(), id.ToString()));
        await reading.Done.WaitAsync(Timeout);

        var types = reading.Events.Select(e => e.Type).ToList();
        var taken = reading.Events.Single(e => e.Type == CliAgentEventType.UserMessage);
        taken.UserMessage!.Taken.ShouldBeTrue();
        taken.UserMessage.MessageId.ShouldBe(id);
        taken.UserMessage.Text.ShouldBe("also run the linter");
        types.IndexOf(CliAgentEventType.UserMessage).ShouldBeGreaterThan(types.IndexOf(CliAgentEventType.ToolResult), "taken after the tool result");
        types.IndexOf(CliAgentEventType.UserMessage).ShouldBeLessThan(types.LastIndexOf(CliAgentEventType.Text), "and before the answer to it");
        types.ShouldNotContain(CliAgentEventType.TurnCompleted, "it was folded into the turn, which ended once");
        adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Status.ShouldBe(CliRunStatus.Completed);
    }

    /// <summary>
    /// The message arrives with the turn's last answer: claude ends that turn, then gives the message a turn of its own.
    /// The result in between is not the end of the reading (it would close stdin and the message would be lost).
    /// </summary>
    [Fact]
    public async Task AMessageThatMissedTheTurnsEnd_GetsATurnOfItsOwn_AndTheReadingGoesOn()
    {
        var adapter = new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance);
        var transport = new FakeCliAgentTransport([], onWrite: line => UuidOf(line) is { } uuid
            ? [Lifecycle(uuid, "queued")]
            : [Init(), Text("msg_1", "Both commands ran.")]);
        var reading = Reading.Start(adapter, transport);
        await reading.UntilAsync(e => e.Any(x => x.Type == CliAgentEventType.Text));

        var id = Guid.NewGuid();
        (await adapter.SendUserMessageAsync(new CliTurnInputMessage(id, "end with PINEAPPLE", []), CancellationToken.None)).ShouldBeTrue();
        transport.Emit(Result("Both commands ran.", "first-prompt"));
        await reading.UntilAsync(e => e.Any(x => x.Type == CliAgentEventType.TurnCompleted));
        reading.Done.IsCompleted.ShouldBeFalse("claude answers the message in a turn after this result");

        transport.Emit(
            Lifecycle(id.ToString(), "started"),
            Init(),
            Text("msg_2", "Both commands ran. PINEAPPLE"),
            Result("Both commands ran. PINEAPPLE", id.ToString()),
            Lifecycle(id.ToString(), "completed"));
        await reading.Done.WaitAsync(Timeout);

        reading.Events.Select(e => e.Type).Where(t => t is CliAgentEventType.TurnCompleted or CliAgentEventType.UserMessage)
            .ShouldBe([CliAgentEventType.TurnCompleted, CliAgentEventType.UserMessage]);
        reading.Events.Single(e => e.Type == CliAgentEventType.UserMessage).UserMessage!.Taken.ShouldBeTrue();
        adapter.IsAwaitingFollowUp.ShouldBeFalse("waiting for a written message's turn is not a pause the process may be handed over in");
        adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Output.ShouldBe("Both commands ran. PINEAPPLE");
    }

    [Fact]
    public async Task AMessageTheCliDeclines_IsReportedNotTaken()
    {
        var adapter = new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance);
        var transport = new FakeCliAgentTransport([], onWrite: line => UuidOf(line) is { } uuid
            ? [Lifecycle(uuid, "refused")]
            : [Init(), ToolUse("msg_1", "call_1", "Bash")]);
        var reading = Reading.Start(adapter, transport);
        await reading.UntilAsync(e => e.Any(x => x.Type == CliAgentEventType.ToolUse));

        var id = Guid.NewGuid();
        await adapter.SendUserMessageAsync(new CliTurnInputMessage(id, "and deploy", []), CancellationToken.None);
        await reading.UntilAsync(e => e.Any(x => x.Type == CliAgentEventType.UserMessage));
        transport.Emit(ToolResult("call_1", "ok"), Text("msg_2", "Done."), Result("Done."));
        await reading.Done.WaitAsync(Timeout);

        var declined = reading.Events.Single(e => e.Type == CliAgentEventType.UserMessage).UserMessage!;
        declined.Taken.ShouldBeFalse();
        declined.MessageId.ShouldBe(id);
        declined.Reason.ShouldNotBeNullOrWhiteSpace();
        reading.Events.ShouldNotContain(e => e.Type == CliAgentEventType.TurnCompleted, "a declined message leaves no turn to wait for");
    }

    /// <summary>A CLI that sends no lifecycle frames: the result's <c>user_message_uuids</c> is what says it was taken in.</summary>
    [Fact]
    public async Task WithoutLifecycleFrames_TheResultsListSaysTheMessageWasTaken()
    {
        var adapter = new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance);
        var transport = new FakeCliAgentTransport([], onWrite: line => UuidOf(line) is null ? [Init(), ToolUse("msg_1", "call_1", "Bash")] : []);
        var reading = Reading.Start(adapter, transport);
        await reading.UntilAsync(e => e.Any(x => x.Type == CliAgentEventType.ToolUse));

        var id = Guid.NewGuid();
        await adapter.SendUserMessageAsync(new CliTurnInputMessage(id, "and the linter", []), CancellationToken.None);
        transport.Emit(ToolResult("call_1", "ok"), Text("msg_2", "Linted too."), Result("Linted too.", "first", id.ToString()));
        await reading.Done.WaitAsync(Timeout);

        reading.Events.Single(e => e.Type == CliAgentEventType.UserMessage).UserMessage!.Taken.ShouldBeTrue();
    }

    [Fact]
    public async Task NothingIsWritten_BeforeThePromptOrAfterTheLastResult()
    {
        var adapter = new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance);
        var transport = new FakeCliAgentTransport([], onWrite: line => UuidOf(line) is null ? [Init(), Text("msg_1", "Hi."), Result("Hi.")] : []);

        (await adapter.SendUserMessageAsync(new CliTurnInputMessage(Guid.NewGuid(), "early", []), CancellationToken.None))
            .ShouldBeFalse("before the prompt it would become the first turn");

        var reading = Reading.Start(adapter, transport);
        await reading.Done.WaitAsync(Timeout);

        (await adapter.SendUserMessageAsync(new CliTurnInputMessage(Guid.NewGuid(), "late", []), CancellationToken.None))
            .ShouldBeFalse("the process no longer reads");
        transport.Written.ShouldHaveSingleItem("only the prompt");
    }

    [Fact]
    public async Task AnImage_GoesWithTheMessage_AsInThePrompt()
    {
        var adapter = new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance);
        var transport = new FakeCliAgentTransport([], onWrite: line => UuidOf(line) is null ? [Init(), ToolUse("msg_1", "call_1", "Bash")] : []);
        var reading = Reading.Start(adapter, transport);
        await reading.UntilAsync(e => e.Any(x => x.Type == CliAgentEventType.ToolUse));

        byte[] png = [0x89, 0x50, 0x4E, 0x47];
        await adapter.SendUserMessageAsync(
            new CliTurnInputMessage(Guid.NewGuid(), "like this one", [new CliPromptAttachment("image/png", "shot.png", png)]), CancellationToken.None);

        using (var written = JsonDocument.Parse(transport.Written[^1]))
        {
            var content = written.RootElement.GetProperty("message").GetProperty("content");
            content[0].GetProperty("type").GetString().ShouldBe("image");
            content[0].GetProperty("source").GetProperty("data").GetString().ShouldBe(Convert.ToBase64String(png));
            content[1].GetProperty("text").GetString().ShouldBe("like this one");
        }

        transport.CompleteOutput();
        await reading.Done.WaitAsync(Timeout);
    }
}
