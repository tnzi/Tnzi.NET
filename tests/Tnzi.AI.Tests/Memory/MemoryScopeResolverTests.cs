namespace Tnzi.AI.Tests.Memory;

public class MemoryScopeResolverTests
{
    [Fact]
    public void BuildLocalScope_UserAndAgentIsolation_AppliesConfiguredDimensions()
    {
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        var scope = MemoryScopeResolver.BuildLocalScope(
            "default",
            enableUserIsolation: true,
            enableAgentIsolation: true,
            userId,
            agentId);

        scope.Name.ShouldBe("default");
        scope.UserId.ShouldBe(userId);
        scope.AgentId.ShouldBe(agentId);
    }

    [Fact]
    public void ResolveProjectSnapshotScope_ReturnsStableDerivedScope()
    {
        var first = MemoryScopeResolver.ResolveProjectSnapshotScope(
            enableProjectSnapshot: true,
            projectSnapshotScopePrefix: "project",
            projectRoot: @"D:\Repo\Tnzi.NET");
        var second = MemoryScopeResolver.ResolveProjectSnapshotScope(
            enableProjectSnapshot: true,
            projectSnapshotScopePrefix: "project",
            projectRoot: @"D:\Repo\Tnzi.NET\.");

        first.ShouldNotBeNull();
        second.ShouldBe(first);
        first.ShouldStartWith("project:tnzi-net:");
    }

    [Fact]
    public void BuildAgentTypeScope_SameType_SameScope()
    {
        var scope1 = MemoryScopeResolver.BuildAgentTypeScope("researcher");
        var scope2 = MemoryScopeResolver.BuildAgentTypeScope("Researcher");

        scope1.ToScopeKey().ShouldBe(scope2.ToScopeKey());
        scope1.Name.ShouldBe("agent-type:researcher");
    }

    [Fact]
    public void BuildAgentTypeScope_WithUserId_IncludesUserIsolation()
    {
        var userId = Guid.NewGuid();
        var scope = MemoryScopeResolver.BuildAgentTypeScope("coder", userId);

        scope.UserId.ShouldBe(userId);
        scope.Name.ShouldBe("agent-type:coder");
        scope.ToScopeKey().ShouldContain($"user:{userId:N}");
    }

    [Fact]
    public void BuildAgentTypeScope_DifferentTypes_DifferentScopes()
    {
        var scope1 = MemoryScopeResolver.BuildAgentTypeScope("researcher");
        var scope2 = MemoryScopeResolver.BuildAgentTypeScope("coder");

        scope1.ToScopeKey().ShouldNotBe(scope2.ToScopeKey());
    }

    [Fact]
    public void BuildAgentTypeScope_EmptyType_DefaultsToDefault()
    {
        var scope = MemoryScopeResolver.BuildAgentTypeScope("  ");

        scope.Name.ShouldBe("agent-type:default");
    }

    [Fact]
    public void BuildLocalOverrideScope_ContainsUserId()
    {
        var userId = Guid.NewGuid();
        var scope = MemoryScopeResolver.BuildLocalOverrideScope(userId);

        scope.Name.ShouldBe($"local:{userId:N}");
        scope.UserId.ShouldBe(userId);
    }

    [Fact]
    public void BuildLocalOverrideScope_WithAgentId_IncludesAgentIsolation()
    {
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var scope = MemoryScopeResolver.BuildLocalOverrideScope(userId, agentId);

        scope.AgentId.ShouldBe(agentId);
        scope.ToScopeKey().ShouldContain($"agent:{agentId:N}");
    }

    [Fact]
    public void BuildLayeredScopes_ReturnsCorrectOrder()
    {
        var userId = Guid.NewGuid();
        var scopes = MemoryScopeResolver.BuildLayeredScopes(userId, projectSnapshotScope: "project:test:abc123");

        scopes.Count.ShouldBe(3);
        scopes[0].Name.ShouldStartWith("local:");
        scopes[1].Name.ShouldBe("project:test:abc123");
        scopes[2].Name.ShouldBe("default");
    }

    [Fact]
    public void BuildLayeredScopes_NoProjectSnapshot_ReturnsTwoScopes()
    {
        var userId = Guid.NewGuid();
        var scopes = MemoryScopeResolver.BuildLayeredScopes(userId);

        scopes.Count.ShouldBe(2);
        scopes[0].Name.ShouldStartWith("local:");
        scopes[1].Name.ShouldBe("default");
    }

    [Fact]
    public void BuildLayeredScopes_CustomSystemScope()
    {
        var userId = Guid.NewGuid();
        var scopes = MemoryScopeResolver.BuildLayeredScopes(userId, systemScopeName: "global");

        scopes[^1].Name.ShouldBe("global");
    }

    // -------------------------------------------------------------------------
    // SnapshotSyncMetadata
    // -------------------------------------------------------------------------

    [Fact]
    public void GetSnapshotMetadata_ReturnsMetadata_ForValidInputs()
    {
        var scopeName = MemoryScopeResolver.ResolveProjectSnapshotScope(
            enableProjectSnapshot: true,
            projectSnapshotScopePrefix: "project",
            projectRoot: @"D:\Repo\Tnzi.NET");

        scopeName.ShouldNotBeNull();

        var metadata = MemoryScopeResolver.GetSnapshotMetadata(scopeName, @"D:\Repo\Tnzi.NET");

        metadata.ShouldNotBeNull();
        metadata!.ScopeName.ShouldBe(scopeName);
        metadata.SourcePath.ShouldNotBeNullOrWhiteSpace();
        metadata.ContentHash.ShouldNotBeNullOrWhiteSpace();
        metadata.ContentHash.Length.ShouldBe(64); // SHA-256 = 64 hex chars
        metadata.IsOverridden.ShouldBeFalse();
    }

    [Fact]
    public void GetSnapshotMetadata_DetectsOverridden_WhenHashMismatch()
    {
        // Use a scope name from one path but metadata from a different path
        var scopeName = MemoryScopeResolver.ResolveProjectSnapshotScope(
            enableProjectSnapshot: true,
            projectSnapshotScopePrefix: "project",
            projectRoot: @"D:\Repo\ProjectA");

        scopeName.ShouldNotBeNull();

        var metadata = MemoryScopeResolver.GetSnapshotMetadata(scopeName, @"D:\Repo\ProjectB");

        metadata.ShouldNotBeNull();
        metadata!.IsOverridden.ShouldBeTrue();
    }

    [Fact]
    public void GetSnapshotMetadata_ReturnsNull_ForNullInputs()
    {
        MemoryScopeResolver.GetSnapshotMetadata(null, @"D:\Repo").ShouldBeNull();
        MemoryScopeResolver.GetSnapshotMetadata("project:test:abc", null).ShouldBeNull();
        MemoryScopeResolver.GetSnapshotMetadata("", @"D:\Repo").ShouldBeNull();
        MemoryScopeResolver.GetSnapshotMetadata("project:test:abc", "  ").ShouldBeNull();
    }

    [Fact]
    public async Task GetSnapshotMetadataAsync_DelegatesToSync()
    {
        var scopeName = MemoryScopeResolver.ResolveProjectSnapshotScope(
            enableProjectSnapshot: true,
            projectSnapshotScopePrefix: "project",
            projectRoot: @"D:\Repo\Test");

        var syncResult = MemoryScopeResolver.GetSnapshotMetadata(scopeName, @"D:\Repo\Test");
        var asyncResult = await MemoryScopeResolver.GetSnapshotMetadataAsync(scopeName, @"D:\Repo\Test");

        asyncResult.ShouldNotBeNull();
        asyncResult!.ScopeName.ShouldBe(syncResult!.ScopeName);
        asyncResult.SourcePath.ShouldBe(syncResult.SourcePath);
        asyncResult.ContentHash.ShouldBe(syncResult.ContentHash);
        asyncResult.IsOverridden.ShouldBe(syncResult.IsOverridden);
    }
}
