namespace Tnzi.AI.Tests.Workspace;

/// <summary>
/// 管理端工作区列表与运行时回退读同一份配置 <c>AI:Workspace</c>。此前列表注入的 <c>IOptions&lt;WorkspaceOptions&gt;</c>
/// 从未绑定任何配置节：配置了 GlobalPath 时，列表扫的是默认目录，而运行时加载的是配置的目录。
/// </summary>
public class WorkspaceAgentAdminServiceTests : IDisposable
{
    private readonly string _configuredRoot;

    public WorkspaceAgentAdminServiceTests()
    {
        _configuredRoot = Path.Combine(Path.GetTempPath(), "tnzi-ws-admin-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_configuredRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_configuredRoot, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响结论
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ListAgentsAsync_ReadsTheConfiguredGlobalPath()
    {
        await WriteAgentAsync(_configuredRoot, "reviewer", "Configured Reviewer");
        var service = CreateService(new WorkspaceOptions { GlobalPath = _configuredRoot, ScanProjectPath = false });

        var result = await service.ListAgentsAsync();

        result.Succeeded.ShouldBeTrue();
        var agents = result.Data!;
        agents.Select(a => a.Name).ShouldContain("Configured Reviewer");
        agents.ShouldAllBe(a => a.WorkspaceScope == "Global");
    }

    [Fact]
    public async Task ListAgentsAsync_DisabledInConfiguration_ListsNothing()
    {
        await WriteAgentAsync(_configuredRoot, "reviewer", "Configured Reviewer");
        var service = CreateService(new WorkspaceOptions { Enabled = false, GlobalPath = _configuredRoot });

        var result = await service.ListAgentsAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data!.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListAgentsAsync_Cancelled_PropagatesTheCancellation()
    {
        await WriteAgentAsync(_configuredRoot, "reviewer", "Configured Reviewer");
        var service = CreateService(new WorkspaceOptions { GlobalPath = _configuredRoot, ScanProjectPath = false });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => service.ListAgentsAsync(cts.Token));
    }

    private static WorkspaceAgentAdminService CreateService(WorkspaceOptions workspace)
        => new(
            new WorkspaceAgentProvider(NullLogger<WorkspaceAgentProvider>.Instance),
            new StaticOptionsMonitor<AIOptions>(new AIOptions { Workspace = workspace }),
            NullLogger<WorkspaceAgentAdminService>.Instance);

    private static async Task WriteAgentAsync(string root, string folder, string name)
    {
        var dir = Path.Combine(root, folder);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "AGENT.md"), $"""
            ---
            name: {name}
            ---
            You review code.
            """);
    }
}
