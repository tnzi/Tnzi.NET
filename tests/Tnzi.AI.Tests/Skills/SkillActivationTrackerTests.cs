namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// <see cref="SkillActivationTracker"/>: the in-memory set plus its round trip through thread metadata.
/// </summary>
public class SkillActivationTrackerTests
{
    private static SkillDefinition Skill(string slug, int priority = 0) => new() { Slug = slug, Name = slug, Content = "x", Priority = priority };

    [Fact]
    public void Activate_ReplacesSameSlugCaseInsensitively_DeactivateRemoves()
    {
        var tracker = new SkillActivationTracker();

        tracker.Activate(Skill("ro"));
        tracker.Activate(Skill("RO", priority: 5));

        tracker.ActivatedSkills.Count.ShouldBe(1);
        tracker.ActivatedSkills[0].Priority.ShouldBe(5);
        tracker.IsActivated("Ro").ShouldBeTrue();

        tracker.Deactivate("rO").ShouldBeTrue();
        tracker.Deactivate("ro").ShouldBeFalse();
        tracker.ActivatedSkills.ShouldBeEmpty();
    }

    [Fact]
    public async Task RestoreAsync_ResolvesPersistedSlugs_SkipsUnknown_AndRunsOncePerScope()
    {
        var threadId = Guid.NewGuid();
        var store = new SkillContextProviderTests.InMemoryThreadMetadata();
        await store.SetMetadataValueAsync(threadId, SkillActivationTracker.ThreadMetadataKey, """["ro","deleted-skill"]""");

        var registry = new Mock<ISkillRegistry>();
        registry.Setup(r => r.GetBySlugAsync("ro", It.IsAny<CancellationToken>())).ReturnsAsync(Skill("ro"));
        registry.Setup(r => r.GetBySlugAsync("deleted-skill", It.IsAny<CancellationToken>())).ReturnsAsync((SkillDefinition?)null);

        var tracker = new SkillActivationTracker(threadService: store, registry: registry.Object);

        await tracker.RestoreAsync(threadId);
        await tracker.RestoreAsync(threadId);

        tracker.ActivatedSkills.Select(s => s.Slug).ShouldBe(["ro"]);
        registry.Verify(r => r.GetBySlugAsync("ro", It.IsAny<CancellationToken>()), Times.Once, "restore is idempotent within a scope");
    }

    [Fact]
    public async Task RestoreAsync_DoesNotOverwriteSkillsActivatedEarlierInTheScope()
    {
        var threadId = Guid.NewGuid();
        var store = new SkillContextProviderTests.InMemoryThreadMetadata();
        await store.SetMetadataValueAsync(threadId, SkillActivationTracker.ThreadMetadataKey, """["ro"]""");
        var registry = new Mock<ISkillRegistry>();
        registry.Setup(r => r.GetBySlugAsync("ro", It.IsAny<CancellationToken>())).ReturnsAsync(Skill("ro"));

        var tracker = new SkillActivationTracker(threadService: store, registry: registry.Object);
        tracker.Activate(Skill("ro", priority: 9));

        await tracker.RestoreAsync(threadId);

        tracker.ActivatedSkills.Single().Priority.ShouldBe(9);
    }

    [Fact]
    public async Task PersistAsync_WritesSlugArray_AndRemovesKeyWhenEmpty()
    {
        var threadId = Guid.NewGuid();
        var store = new SkillContextProviderTests.InMemoryThreadMetadata();
        var tracker = new SkillActivationTracker(threadService: store);

        tracker.Activate(Skill("a"));
        tracker.Activate(Skill("b"));
        await tracker.PersistAsync(threadId);
        JsonSerializer.Deserialize<string[]>(store.Values[(threadId, SkillActivationTracker.ThreadMetadataKey)]).ShouldBe(["a", "b"]);

        tracker.Deactivate("a");
        tracker.Deactivate("b");
        await tracker.PersistAsync(threadId);
        store.Values.ShouldNotContainKey((threadId, SkillActivationTracker.ThreadMetadataKey));
    }

    [Fact]
    public async Task PersistAsync_FailureIsLoggedNotThrown_InMemorySetStaysIntact()
    {
        var threadId = Guid.NewGuid();
        var broken = new Mock<IAgentThreadInternalService>();
        broken.Setup(s => s.SetMetadataValueAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));
        var registry = new Mock<ISkillRegistry>();

        var tracker = new SkillActivationTracker(threadService: broken.Object, registry: registry.Object);
        tracker.Activate(Skill("ro"));

        await Should.NotThrowAsync(() => tracker.PersistAsync(threadId));

        tracker.IsActivated("ro").ShouldBeTrue("the current run's constraints do not depend on persistence");
    }

    /// <summary>
    /// A failed read used to be swallowed after the thread had already been marked restored, so the
    /// scope ran unconstrained and no later caller could retry. Now the failure propagates (the caller
    /// decides whether the turn may proceed) and the next call reads again.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_ReadFailure_ThrowsAndIsNotRememberedAsRestored()
    {
        var threadId = Guid.NewGuid();
        var attempts = 0;
        var flaky = new Mock<IAgentThreadInternalService>();
        flaky.Setup(s => s.GetMetadataValueAsync(threadId, SkillActivationTracker.ThreadMetadataKey, It.IsAny<CancellationToken>()))
            .Returns(() => ++attempts == 1
                ? Task.FromException<string?>(new InvalidOperationException("db down"))
                : Task.FromResult<string?>("""["ro"]"""));
        var registry = new Mock<ISkillRegistry>();
        registry.Setup(r => r.GetBySlugAsync("ro", It.IsAny<CancellationToken>())).ReturnsAsync(Skill("ro"));

        var tracker = new SkillActivationTracker(threadService: flaky.Object, registry: registry.Object);

        await Should.ThrowAsync<InvalidOperationException>(() => tracker.RestoreAsync(threadId));
        tracker.ActivatedSkills.ShouldBeEmpty();

        await tracker.RestoreAsync(threadId);

        tracker.ActivatedSkills.Select(s => s.Slug).ShouldBe(["ro"], "the second caller must retry the read instead of inheriting the failed attempt");
        attempts.ShouldBe(2);
    }

    /// <summary>
    /// The scope is marked restored only once the whole set resolved: a registry failure halfway
    /// (swallowed by the provider's try/catch at 400) must not leave a half-restored set that 450
    /// then accepts as complete.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_RegistryFailsMidway_NextCallResumesAndCompletes()
    {
        var threadId = Guid.NewGuid();
        var store = new SkillContextProviderTests.InMemoryThreadMetadata();
        await store.SetMetadataValueAsync(threadId, SkillActivationTracker.ThreadMetadataKey, """["a","b"]""");
        var bAttempts = 0;
        var registry = new Mock<ISkillRegistry>();
        registry.Setup(r => r.GetBySlugAsync("a", It.IsAny<CancellationToken>())).ReturnsAsync(Skill("a"));
        registry.Setup(r => r.GetBySlugAsync("b", It.IsAny<CancellationToken>()))
            .Returns(() => ++bAttempts == 1
                ? Task.FromException<SkillDefinition?>(new InvalidOperationException("registry down"))
                : Task.FromResult<SkillDefinition?>(Skill("b")));
        var tracker = new SkillActivationTracker(threadService: store, registry: registry.Object);

        await Should.ThrowAsync<InvalidOperationException>(() => tracker.RestoreAsync(threadId));
        tracker.ActivatedSkills.Select(s => s.Slug).ShouldBe(["a"], "precondition: the first attempt stopped halfway");

        await tracker.RestoreAsync(threadId);

        tracker.ActivatedSkills.Select(s => s.Slug).ShouldBe(["a", "b"]);
        registry.Verify(r => r.GetBySlugAsync("a", It.IsAny<CancellationToken>()), Times.Once, "already-restored slugs are not re-resolved");
    }

    [Fact]
    public async Task RestoreAsync_CorruptMetadata_Throws()
    {
        var threadId = Guid.NewGuid();
        var store = new SkillContextProviderTests.InMemoryThreadMetadata();
        await store.SetMetadataValueAsync(threadId, SkillActivationTracker.ThreadMetadataKey, "not json");
        var tracker = new SkillActivationTracker(threadService: store, registry: Mock.Of<ISkillRegistry>());

        await Should.ThrowAsync<JsonException>(() => tracker.RestoreAsync(threadId));
    }

    [Fact]
    public async Task WithoutThreadServiceOrRegistry_RestoreIsNoOp_PersistIsNoOp()
    {
        var tracker = new SkillActivationTracker();
        tracker.Activate(Skill("ro"));

        await tracker.RestoreAsync(Guid.NewGuid());
        await tracker.PersistAsync(Guid.NewGuid());

        tracker.ActivatedSkills.Count.ShouldBe(1);
    }
}
