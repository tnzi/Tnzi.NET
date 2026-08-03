using ModelContextProtocol.Protocol;
using Tnzi.AI.Mcp.Controllers.Admin;
using Tnzi.AI.Mcp.Server;
using McpServerOptions = Tnzi.AI.Mcp.Options.McpServerOptions;

namespace Tnzi.AI.Tests.Mcp;

/// <summary>
/// DefaultMcpAdminController 单元测试 - status / tools / agents 查询 + 动态 expose / remove。
/// 直接构造控制器，注入 IMcpServerHost / IOptions&lt;McpServerOptions&gt; 模拟。
/// </summary>
public class DefaultMcpAdminControllerTests
{
    private readonly Mock<IMcpServerHost> _host = new();

    private DefaultMcpAdminController CreateController(McpServerOptions? options = null) =>
        new(_host.Object, new StaticOptionsMonitor<McpServerOptions>(options ?? new McpServerOptions()));

    // ─── GetStatus ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatus_ReportsOptionsAndToolCounts()
    {
        var options = new McpServerOptions
        {
            Enabled = true,
            Endpoint = "/mcp",
            RequireAuthentication = true,
            RateLimitPerTenant = true,
            RateLimitPerMinute = 600
        };

        _host.Setup(h => h.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Tool { Name = "agent-a", Description = "Agent A" },
                new Tool { Name = "echo", Description = "Echo" }
            ]);
        _host.Setup(h => h.GetExposedAgentIds()).Returns([Guid.NewGuid()]);
        _host.Setup(h => h.GetCustomToolNames()).Returns(["echo"]);

        var controller = CreateController(options);

        var result = await controller.GetStatus();

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Enabled.ShouldBeTrue();
        result.Data.Endpoint.ShouldBe("/mcp");
        result.Data.RequireAuthentication.ShouldBeTrue();
        result.Data.RateLimitPerMinute.ShouldBe(600);
        result.Data.ExposedAgentCount.ShouldBe(1);
        result.Data.CustomToolCount.ShouldBe(1);
        result.Data.TotalToolCount.ShouldBe(2);
    }

    // ─── GetTools ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTools_ProjectsNameAndDescription()
    {
        _host.Setup(h => h.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Tool { Name = "agent-a", Description = "Agent A" },
                new Tool { Name = "echo", Description = "Echo" }
            ]);

        var controller = CreateController();

        var result = await controller.GetTools();

        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(2);
        result.Data.Select(t => t.Name).ShouldBe(["agent-a", "echo"]);
        result.Data.Single(t => t.Name == "echo").Description.ShouldBe("Echo");
    }

    // ─── GetExposedAgents ──────────────────────────────────────────────────────

    [Fact]
    public void GetExposedAgents_ReturnsHostExposedIds()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        _host.Setup(h => h.GetExposedAgentIds()).Returns([a, b]);

        var controller = CreateController();

        var result = controller.GetExposedAgents();

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe([a, b]);
    }

    // ─── ExposeAgent ───────────────────────────────────────────────────────────

    [Fact]
    public void ExposeAgent_DelegatesToHost()
    {
        var agentId = Guid.NewGuid();
        var opts = new McpToolExposureOptions { ToolName = "custom-name" };
        var controller = CreateController();

        var result = controller.ExposeAgent(agentId, opts);

        result.Succeeded.ShouldBeTrue();
        _host.Verify(h => h.ExposeAgent(agentId, opts), Times.Once);
    }

    [Fact]
    public void ExposeAgent_NullOptions_StillDelegates()
    {
        var agentId = Guid.NewGuid();
        var controller = CreateController();

        var result = controller.ExposeAgent(agentId, null);

        result.Succeeded.ShouldBeTrue();
        _host.Verify(h => h.ExposeAgent(agentId, null), Times.Once);
    }

    // ─── RemoveAgent ─────────────────────────────────────────────────────────────

    [Fact]
    public void RemoveAgent_Found_ReturnsOk()
    {
        var agentId = Guid.NewGuid();
        _host.Setup(h => h.RemoveAgent(agentId)).Returns(true);
        var controller = CreateController();

        var result = controller.RemoveAgent(agentId);

        result.Succeeded.ShouldBeTrue();
        _host.Verify(h => h.RemoveAgent(agentId), Times.Once);
    }

    [Fact]
    public void RemoveAgent_NotFound_Returns404()
    {
        var agentId = Guid.NewGuid();
        _host.Setup(h => h.RemoveAgent(agentId)).Returns(false);
        var controller = CreateController();

        var result = controller.RemoveAgent(agentId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }
}
