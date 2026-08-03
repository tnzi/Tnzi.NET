namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// The streaming path concatenates every text delta of a turn, so "let me check the logs"
/// ends up glued to the answer. These tests pin how the answer is pulled back out.
/// </summary>
public class DeliverableTrackerTests
{
    [Fact]
    public void Resolve_ReturnsNull_WhenNoToolCallsOccurred()
    {
        var tracker = new DeliverableTracker();
        tracker.Observe(new AgentStreamChunk { Text = "The answer is 42." });

        // Null means "the deliverable is the whole response" - the overwhelmingly common case,
        // and it keeps consumers from having to know which execution path produced the result.
        tracker.Resolve("The answer is 42.").ShouldBeNull();
    }

    [Fact]
    public void Resolve_KeepsOnlyTextAfterTheLastToolCall()
    {
        var tracker = new DeliverableTracker();
        tracker.Observe(new AgentStreamChunk { Text = "Let me check the logs. " });
        tracker.Observe(new AgentStreamChunk { IsToolCall = true, ToolCallNames = ["read_file"] });
        tracker.Observe(new AgentStreamChunk { Text = "The deploy failed on step 3." });

        var full = "Let me check the logs. The deploy failed on step 3.";

        tracker.Resolve(full).ShouldBe("The deploy failed on step 3.");
    }

    [Fact]
    public void Resolve_KeepsOnlyTextAfterTheLastOfSeveralToolCalls()
    {
        var tracker = new DeliverableTracker();
        tracker.Observe(new AgentStreamChunk { Text = "First I'll look at the config. " });
        tracker.Observe(new AgentStreamChunk { IsToolCall = true });
        tracker.Observe(new AgentStreamChunk { Text = "Now the logs. " });
        tracker.Observe(new AgentStreamChunk { IsToolCall = true });
        tracker.Observe(new AgentStreamChunk { Text = "Root cause: the port was already bound." });

        var full = "First I'll look at the config. Now the logs. Root cause: the port was already bound.";

        // Not just "after the first tool call" - narration accumulates between every round.
        tracker.Resolve(full).ShouldBe("Root cause: the port was already bound.");
    }

    [Fact]
    public void Resolve_FallsBackToThePreviousBlock_WhenNothingWasSaidAfterTheLastToolCall()
    {
        var tracker = new DeliverableTracker();
        tracker.Observe(new AgentStreamChunk { Text = "Applying the migration now." });
        tracker.Observe(new AgentStreamChunk { IsToolCall = true });

        // An empty deliverable would render as a blank outbound message, which is worse than
        // one extra sentence of narration.
        tracker.Resolve("Applying the migration now.").ShouldBeNull();
    }

    [Fact]
    public void Resolve_UsesTheMostRecentBlock_WhenATurnEndsOnAToolCall()
    {
        var tracker = new DeliverableTracker();
        tracker.Observe(new AgentStreamChunk { Text = "Checking. " });
        tracker.Observe(new AgentStreamChunk { IsToolCall = true });
        tracker.Observe(new AgentStreamChunk { Text = "Found it, applying the fix." });
        tracker.Observe(new AgentStreamChunk { IsToolCall = true });

        // Falls back to the last thing actually said, not to the whole transcript.
        tracker.Resolve("Checking. Found it, applying the fix.").ShouldBe("Found it, applying the fix.");
    }

    [Fact]
    public void Observe_TreatsToolCallDetailChunksAsABoundaryToo()
    {
        var tracker = new DeliverableTracker();
        tracker.Observe(new AgentStreamChunk { Text = "Looking into it. " });

        // Tool activity surfaces as two different chunk shapes (the early IsToolCall signal and
        // the post-execution ToolCalls detail). Missing either one leaks narration.
        tracker.Observe(new AgentStreamChunk
        {
            ToolCalls = [new ToolCallDetail { Name = "bash", DurationMs = 12 }]
        });
        tracker.Observe(new AgentStreamChunk { Text = "Disk was full." });

        tracker.Resolve("Looking into it. Disk was full.").ShouldBe("Disk was full.");
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenTheTurnProducedNoTextAtAll()
    {
        var tracker = new DeliverableTracker();
        tracker.Observe(new AgentStreamChunk { IsToolCall = true });

        tracker.Resolve(string.Empty).ShouldBeNull();
    }

    [Fact]
    public void CloneWith_DropsTheDeliverable_WhenTheResponseIsRewritten()
    {
        var original = new AgentRunResult
        {
            Response = "Checking. Your card number is 4111-1111-1111-1111.",
            Deliverable = "Your card number is 4111-1111-1111-1111."
        };

        // An output guardrail redacting the response must not leave the pre-redaction text
        // reachable through EffectiveDeliverable - the guardrail would still look like it worked.
        var redacted = original.CloneWith(response: "Checking. Your card number is ****.");

        redacted.Deliverable.ShouldBeNull();
        redacted.EffectiveDeliverable.ShouldBe("Checking. Your card number is ****.");
    }

    [Fact]
    public void CloneWith_KeepsTheDeliverable_WhenTheResponseIsUntouched()
    {
        var original = new AgentRunResult { Response = "narration. answer.", Deliverable = "answer." };

        // The overwhelmingly common case: middlewares annotate a result without rewriting its text.
        var annotated = original.CloneWith(status: AgentRunStatus.Completed);

        annotated.Deliverable.ShouldBe("answer.");
    }

    [Fact]
    public void CloneWith_AcceptsAnExplicitReplacementDeliverable()
    {
        var original = new AgentRunResult { Response = "old", Deliverable = "old deliverable" };

        var rewritten = original.CloneWith(response: "new", deliverable: "new deliverable");

        rewritten.Deliverable.ShouldBe("new deliverable");
    }
}
