using Tnzi.AI.Memory;

namespace Tnzi.AI.Tests.Memory;

/// <summary>
/// MemoryScope record 单元测试 — ToScopeKey 生成 + 隐式转换
/// </summary>
public class MemoryScopeTests
{
    [Fact]
    public void ToScopeKey_NameOnly_ReturnsName()
    {
        var scope = new MemoryScope("default");
        scope.ToScopeKey().ShouldBe("default");
    }

    [Fact]
    public void ToScopeKey_WithUserId_IncludesUserPrefix()
    {
        var userId = Guid.NewGuid();
        var scope = new MemoryScope("default", UserId: userId);

        var key = scope.ToScopeKey();
        key.ShouldStartWith($"user:{userId:N}");
        key.ShouldEndWith(":default");
    }

    [Fact]
    public void ToScopeKey_WithAgentId_IncludesAgentPrefix()
    {
        var agentId = Guid.NewGuid();
        var scope = new MemoryScope("default", AgentId: agentId);

        var key = scope.ToScopeKey();
        key.ShouldStartWith($"agent:{agentId:N}");
        key.ShouldEndWith(":default");
    }

    [Fact]
    public void ToScopeKey_WithSessionId_IncludesSessionPrefix()
    {
        var scope = new MemoryScope("default", SessionId: "sess-123");

        var key = scope.ToScopeKey();
        key.ShouldContain("session:sess-123");
        key.ShouldEndWith(":default");
    }

    [Fact]
    public void ToScopeKey_AllFields_OrdersCorrectly()
    {
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var scope = new MemoryScope("my-scope", userId, agentId, "sess-1");

        var key = scope.ToScopeKey();
        var parts = key.Split(':');

        // user:{guid}:agent:{guid}:session:sess-1:my-scope
        parts[0].ShouldBe("user");
        parts[2].ShouldBe("agent");
        key.ShouldContain("session:sess-1");
        key.ShouldEndWith(":my-scope");
    }

    [Fact]
    public void ImplicitConversion_FromString_CreatesScope()
    {
        MemoryScope scope = "test-scope";

        scope.Name.ShouldBe("test-scope");
        scope.UserId.ShouldBeNull();
        scope.AgentId.ShouldBeNull();
        scope.SessionId.ShouldBeNull();
    }

    [Fact]
    public void ImplicitConversion_ToScopeKey_MatchesName()
    {
        MemoryScope scope = "simple";
        scope.ToScopeKey().ShouldBe("simple");
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        var id = Guid.NewGuid();
        var a = new MemoryScope("test", id);
        var b = new MemoryScope("test", id);

        a.ShouldBe(b);
    }

    [Fact]
    public void RecordEquality_DifferentUserId_AreNotEqual()
    {
        var a = new MemoryScope("test", Guid.NewGuid());
        var b = new MemoryScope("test", Guid.NewGuid());

        a.ShouldNotBe(b);
    }

    // --- Agent-bound 范围（修复 AgentId 列只写不读缺陷）---

    [Fact]
    public void ForAgent_SetsAgentBoundAndAgentId()
    {
        var agentId = Guid.NewGuid();
        var scope = MemoryScope.ForAgent(agentId, "default");

        scope.AgentBound.ShouldBeTrue();
        scope.AgentId.ShouldBe(agentId);
        scope.Name.ShouldBe("default");
        scope.UserId.ShouldBeNull();
        scope.SessionId.ShouldBeNull();
    }

    [Fact]
    public void ForAgent_BlankName_DefaultsToDefault()
    {
        var scope = MemoryScope.ForAgent(Guid.NewGuid(), "  ");
        scope.Name.ShouldBe("default");
    }

    [Fact]
    public void ToScopeKey_AgentBound_IsUserIndependentAndDeterministic()
    {
        var agentId = Guid.NewGuid();
        var scope = MemoryScope.ForAgent(agentId, "default");

        var key = scope.ToScopeKey();

        // 与当前用户无关 → headless 写入/读取一致；不含 user:/session: 段
        key.ShouldBe($"agent-bound:{agentId:N}:default");
        key.ShouldNotContain("user:");
        key.ShouldNotContain("session:");
    }

    [Fact]
    public void ToScopeKey_AgentBound_DiffersFromUserScopedAgentKey()
    {
        var agentId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var agentBound = MemoryScope.ForAgent(agentId, "default").ToScopeKey();
        var userScoped = new MemoryScope("default", userId, agentId).ToScopeKey();

        // 两条路径产生不同的 key —— 这正是修复前消费者记忆永不命中的根因
        agentBound.ShouldNotBe(userScoped);
    }

    [Fact]
    public void AgentBound_DefaultFalse_PreservesLegacyKey()
    {
        // 不显式设置 AgentBound 时，含 AgentId 的 scope 仍生成旧格式 key（向后兼容）
        var agentId = Guid.NewGuid();
        var scope = new MemoryScope("default", AgentId: agentId);

        scope.AgentBound.ShouldBeFalse();
        scope.ToScopeKey().ShouldBe($"agent:{agentId:N}:default");
    }
}
