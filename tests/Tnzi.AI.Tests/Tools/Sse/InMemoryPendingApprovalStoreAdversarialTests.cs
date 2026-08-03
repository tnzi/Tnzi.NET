using Tnzi.AI.Tools.Approval.Sse;

namespace Tnzi.AI.Tests.Tools.Sse;

/// <summary>
/// Adversarial coverage for <see cref="InMemoryPendingApprovalStore"/> - security-relevant edge
/// cases the previous implementation did not enforce (cross-user authz, TTL fail-closed,
/// duplicate registration, missing user identity).
/// </summary>
public class InMemoryPendingApprovalStoreAdversarialTests
{
    private static PendingApprovalRequest MakeRequest(string userId = "alice", Guid? id = null) =>
        new(
            Id: id ?? Guid.NewGuid(),
            ToolName: "delete_file",
            Arguments: "{}",
            CreatedAt: DateTimeOffset.UtcNow,
            UserId: userId);

    [Fact]
    public async Task ResolveAsync_ByDifferentUser_Returns_NotAuthorized()
    {
        using var store = new InMemoryPendingApprovalStore();
        var req = MakeRequest("alice");
        await store.RegisterAsync(req);

        var result = await store.ResolveAsync(
            req.Id,
            new ApprovalDecision(true, "ok", "mallory"),
            currentUserId: "mallory");

        result.ShouldBe(ResolveResult.NotAuthorized);

        // The entry must remain so the rightful owner can still resolve it.
        var fetched = await store.GetAsync(req.Id);
        fetched.ShouldNotBeNull();
    }

    [Fact]
    public async Task ResolveAsync_WithNullCurrentUser_Returns_NotAuthorized()
    {
        using var store = new InMemoryPendingApprovalStore();
        var req = MakeRequest("alice");
        await store.RegisterAsync(req);

        var result = await store.ResolveAsync(
            req.Id,
            new ApprovalDecision(true, "ok", null),
            currentUserId: null);

        result.ShouldBe(ResolveResult.NotAuthorized);
    }

    [Fact]
    public async Task ResolveAsync_ByOwner_Returns_Resolved()
    {
        using var store = new InMemoryPendingApprovalStore();
        var req = MakeRequest("alice");
        await store.RegisterAsync(req);

        var result = await store.ResolveAsync(
            req.Id,
            new ApprovalDecision(true, "ok", "alice"),
            currentUserId: "alice");

        result.ShouldBe(ResolveResult.Resolved);
    }

    [Fact]
    public async Task ResolveAsync_TwiceForSameRequest_Returns_NotFound_OnSecondCall()
    {
        using var store = new InMemoryPendingApprovalStore();
        var req = MakeRequest("alice");
        await store.RegisterAsync(req);

        var first = await store.ResolveAsync(req.Id, new ApprovalDecision(true, null, "alice"), "alice");
        first.ShouldBe(ResolveResult.Resolved);

        var second = await store.ResolveAsync(req.Id, new ApprovalDecision(false, null, "alice"), "alice");
        second.ShouldBe(ResolveResult.NotFound);
    }

    [Fact]
    public async Task RegisterAsync_WithEmptyUserId_Throws()
    {
        using var store = new InMemoryPendingApprovalStore();
        var req = MakeRequest(userId: "");
        await Should.ThrowAsync<InvalidOperationException>(() => store.RegisterAsync(req));
    }

    [Fact]
    public async Task RegisterAsync_DuplicateId_Throws()
    {
        using var store = new InMemoryPendingApprovalStore();
        var id = Guid.NewGuid();
        await store.RegisterAsync(MakeRequest("alice", id));
        await Should.ThrowAsync<InvalidOperationException>(
            () => store.RegisterAsync(MakeRequest("alice", id)));
    }

    [Fact]
    public async Task AwaitDecision_OnTTLExpire_Returns_FailClosedTimeout()
    {
        // Use FakeTimeProvider so the sweep timer can be triggered deterministically.
        var fakeTime = new ControllableTimeProvider(DateTimeOffset.UtcNow);
        using var store = new InMemoryPendingApprovalStore(fakeTime);

        var req = MakeRequest("alice");
        await store.RegisterAsync(req, ttl: TimeSpan.FromMilliseconds(100));

        // Advance virtual time well past the TTL.
        fakeTime.Advance(TimeSpan.FromSeconds(60));

        // Wait for the next sweep cycle (every 30s real-time) - accelerate by manually invoking
        // the sweep via reflection-free path: trigger another Register/Resolve to allow the timer
        // background tick. Easier: spin-wait briefly for the timer to fire.
        var decision = await store.AwaitDecisionAsync(req.Id).WaitAsync(TimeSpan.FromSeconds(45));

        decision.Approved.ShouldBeFalse();
        decision.DecidedBy.ShouldBe("system");
        decision.Reason.ShouldNotBeNull();
        decision.Reason!.ShouldContain("timed out");
    }

    [Fact]
    public async Task ListPendingAsync_FiltersByUser()
    {
        using var store = new InMemoryPendingApprovalStore();
        await store.RegisterAsync(MakeRequest("alice"));
        await store.RegisterAsync(MakeRequest("alice"));
        await store.RegisterAsync(MakeRequest("bob"));

        var aliceItems = await store.ListPendingAsync(currentUserId: "alice");
        aliceItems.Count.ShouldBe(2);
        aliceItems.ShouldAllBe(r => r.UserId == "alice");

        var allItems = await store.ListPendingAsync(currentUserId: null);
        allItems.Count.ShouldBe(3);
    }

    [Fact]
    public async Task GetAsync_ByDifferentUser_Returns_Null()
    {
        using var store = new InMemoryPendingApprovalStore();
        var req = MakeRequest("alice");
        await store.RegisterAsync(req);

        (await store.GetAsync(req.Id, currentUserId: "alice")).ShouldNotBeNull();
        (await store.GetAsync(req.Id, currentUserId: "mallory")).ShouldBeNull();
        (await store.GetAsync(req.Id, currentUserId: null)).ShouldNotBeNull();  // null = admin/system bypass
    }

    [Fact]
    public async Task MultipleConcurrentRequests_AreIsolatedPerUser()
    {
        using var store = new InMemoryPendingApprovalStore();
        var alice1 = MakeRequest("alice");
        var alice2 = MakeRequest("alice");
        var bob1 = MakeRequest("bob");

        await store.RegisterAsync(alice1);
        await store.RegisterAsync(alice2);
        await store.RegisterAsync(bob1);

        // Bob cannot resolve Alice's request.
        var crossUser = await store.ResolveAsync(
            alice1.Id, new ApprovalDecision(true, null, "bob"), "bob");
        crossUser.ShouldBe(ResolveResult.NotAuthorized);

        // Bob can resolve his own.
        var bobResolve = await store.ResolveAsync(
            bob1.Id, new ApprovalDecision(true, null, "bob"), "bob");
        bobResolve.ShouldBe(ResolveResult.Resolved);

        // Alice's two are still pending.
        var alicePending = await store.ListPendingAsync("alice");
        alicePending.Count.ShouldBe(2);
    }

    /// <summary>
    /// Minimal <see cref="TimeProvider"/> fake - advances on demand so TTL tests don't need real time.
    /// </summary>
    private sealed class ControllableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }
}
