namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 交接处的并发槽：进程只有一个，槽也只有一个，跟着进程从等着的运行到接手的运行；没人接手时留在原处。
/// </summary>
public class CliRunHandoffsTests
{
    private static readonly Guid Thread = Guid.NewGuid();

    private static CliLiveSession Session() => new()
    {
        Process = Mock.Of<ICliProcess>(),
        Adapter = Mock.Of<ICliProtocolAdapter>(),
        Events = Mock.Of<IAsyncEnumerator<CliAgentEvent>>(),
        PendingNext = Task.FromResult(false),
        FromRunId = Guid.NewGuid(),
        LaunchedByRunId = Guid.NewGuid(),
        Lifetime = new CancellationTokenSource(),
        Spec = new CliProcessSpec { ExecutablePath = "/bin/claude" },
        Workspace = new CliWorkspace { RootDirectory = "/w", WorkDirectory = "/w", OutputDirectory = "/w", LogDirectory = "/w" }
    };

    [Fact]
    public async Task AHandedOverProcess_TakesTheSlotWithIt()
    {
        var handoffs = new CliRunHandoffs();
        using var slots = new SemaphoreSlim(0, 1);
        var (waitingRun, nextRun) = (Guid.NewGuid(), Guid.NewGuid());
        handoffs.HoldSlot(waitingRun, slots);
        var waiting = handoffs.Offer(waitingRun, Thread, Guid.NewGuid(), Guid.NewGuid());

        var take = handoffs.TakeAsync(Thread, nextRun, CancellationToken.None);
        waiting.Requested.Task.IsCompleted.ShouldBeTrue();
        var session = Session();
        handoffs.Hand(waiting, session).ShouldBeTrue();
        (await take).ShouldBeSameAs(session);

        handoffs.ReleaseSlot(waitingRun);
        slots.CurrentCount.ShouldBe(0, "the run that handed its process over no longer holds the slot");
        handoffs.ReleaseSlot(nextRun);
        slots.CurrentCount.ShouldBe(1);
        handoffs.WaitingRuns().ShouldBeEmpty();
    }

    [Fact]
    public async Task WithNoProcessToHand_TheNextRunGetsNull_AndTheSlotForItsOwnProcess()
    {
        var handoffs = new CliRunHandoffs();
        using var slots = new SemaphoreSlim(0, 1);
        var (waitingRun, nextRun) = (Guid.NewGuid(), Guid.NewGuid());
        handoffs.HoldSlot(waitingRun, slots);
        var waiting = handoffs.Offer(waitingRun, Thread, Guid.NewGuid(), Guid.NewGuid());

        var take = handoffs.TakeAsync(Thread, nextRun, CancellationToken.None);
        handoffs.Hand(waiting, null).ShouldBeTrue();
        (await take).ShouldBeNull();

        handoffs.ReleaseSlot(waitingRun);
        slots.CurrentCount.ShouldBe(0);
        handoffs.ReleaseSlot(nextRun);
        slots.CurrentCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenTheNextRunGaveUp_NothingIsHanded_AndTheSlotStays()
    {
        var handoffs = new CliRunHandoffs();
        using var slots = new SemaphoreSlim(0, 1);
        var (waitingRun, nextRun) = (Guid.NewGuid(), Guid.NewGuid());
        handoffs.HoldSlot(waitingRun, slots);
        var waiting = handoffs.Offer(waitingRun, Thread, Guid.NewGuid(), Guid.NewGuid());

        using var stop = new CancellationTokenSource();
        var take = handoffs.TakeAsync(Thread, nextRun, stop.Token);
        await stop.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => take);
        waiting.Abandoned.ShouldBeTrue();

        handoffs.Hand(waiting, Session()).ShouldBeFalse();
        handoffs.ReleaseSlot(nextRun);
        slots.CurrentCount.ShouldBe(0);
        handoffs.ReleaseSlot(waitingRun);
        slots.CurrentCount.ShouldBe(1);
    }

    [Fact]
    public async Task NothingWaiting_OrAlreadyTaken_IsNull()
    {
        var handoffs = new CliRunHandoffs();
        (await handoffs.TakeAsync(Thread, Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();

        handoffs.Offer(Guid.NewGuid(), Thread, Guid.NewGuid(), Guid.NewGuid());
        _ = handoffs.TakeAsync(Thread, Guid.NewGuid(), CancellationToken.None);
        (await handoffs.TakeAsync(Thread, Guid.NewGuid(), CancellationToken.None)).ShouldBeNull("only the first run to come takes it");
    }
}
