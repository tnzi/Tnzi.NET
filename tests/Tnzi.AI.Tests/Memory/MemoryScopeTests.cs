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
}
