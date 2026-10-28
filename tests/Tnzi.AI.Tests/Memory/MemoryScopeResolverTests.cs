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
        // 路径必须按平台构造。此前写死 @"D:\Repo\Tnzi.NET"：在 Linux 上反斜杠不是
        // 目录分隔符，整个字符串被当成单个文件名，于是「带 \. 与不带」归一化后并不相等，
        // 派生出的 scope 也从 project:tnzi-net: 变成 project:d-repo-tnzi-net:。
        // 这个测试验的是归一化是否稳定，不是 Windows 路径本身。
        var root = Path.Combine(Path.GetTempPath(), "Repo", "Tnzi.NET");

        var first = MemoryScopeResolver.ResolveProjectSnapshotScope(
            enableProjectSnapshot: true,
            projectSnapshotScopePrefix: "project",
            projectRoot: root);
        var second = MemoryScopeResolver.ResolveProjectSnapshotScope(
            enableProjectSnapshot: true,
            projectSnapshotScopePrefix: "project",
            projectRoot: Path.Combine(root, "."));

        first.ShouldNotBeNull();
        second.ShouldBe(first);
        first.ShouldStartWith("project:tnzi-net:");
    }
}
