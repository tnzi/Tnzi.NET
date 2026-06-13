using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.AI.Mcp.Server;
using Tnzi.AI.Mcp.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests;

public class McpServerHostTests
{
    [Fact]
    public async Task ListToolsAsync_UsesConfiguredExposedAgentIds_ForHttpTransport()
    {
        var agentId = Guid.NewGuid();
        var agentService = new Mock<IAgentService>();
        agentService.Setup(x => x.GetByIdAsync(agentId))
            .ReturnsAsync(Result<AgentDto>.Success(new AgentDto
            {
                Id = agentId,
                Name = "Review Agent",
                Description = "Reviews generated output"
            }));

        var services = new ServiceCollection();
        services.AddSingleton(agentService.Object);
        using var serviceProvider = services.BuildServiceProvider();

        var security = new McpServerSecurityMiddleware(
            MsOptions.Create(new McpServerOptions
            {
                Enabled = true,
                ExposedAgentIds = [agentId]
            }),
            NullLogger<McpServerSecurityMiddleware>.Instance,
            serviceProvider);

        var host = new McpServerHost(
            serviceProvider,
            MsOptions.Create(new McpServerOptions
            {
                Enabled = true,
                ExposedAgentIds = [agentId]
            }),
            NullLogger<McpServerHost>.Instance,
            security);

        var tools = await host.ListToolsAsync();

        tools.Count.ShouldBe(1);
        tools[0].Name.ShouldBe("Review_Agent");
        tools[0].Description.ShouldNotBeNull();
        tools[0].Description!.ShouldContain("Reviews generated output");
        agentService.Verify(x => x.GetByIdAsync(agentId), Times.AtLeastOnce);
    }

    /// <summary>
    /// B7: CallToolAsync for a pre-configured agent (in ExposedAgentIds) BEFORE any ListToolsAsync call
    /// must resolve the agent tool by lazily building the name-map, not return "not exposed".
    /// </summary>
    [Fact]
    public async Task CallToolAsync_WithoutPriorListTools_ResolvesExposedAgent_NotNotExposed()
    {
        var agentId = Guid.NewGuid();
        var agentService = new Mock<IAgentService>();
        agentService.Setup(x => x.GetByIdAsync(agentId))
            .ReturnsAsync(Result<AgentDto>.Success(new AgentDto
            {
                Id = agentId,
                Name = "Cold Agent",
                Description = "Cold-cache agent"
            }));

        var agentRuntime = new Mock<IAgentRuntime>();
        agentRuntime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Response = "cold-response" });

        var services = new ServiceCollection();
        services.AddSingleton(agentService.Object);
        services.AddSingleton(agentRuntime.Object);
        using var serviceProvider = services.BuildServiceProvider();

        var options = MsOptions.Create(new McpServerOptions
        {
            Enabled = true,
            ExposedAgentIds = [agentId],
            RequireAuthentication = false,
            RateLimitPerMinute = 0
        });

        var security = new McpServerSecurityMiddleware(
            options,
            NullLogger<McpServerSecurityMiddleware>.Instance,
            serviceProvider);

        var host = new McpServerHost(
            serviceProvider,
            options,
            NullLogger<McpServerHost>.Instance,
            security);

        // No ListToolsAsync call — cold cache
        var result = await host.CallToolAsync(
            "Cold_Agent",
            new Dictionary<string, JsonElement> { ["message"] = JsonSerializer.SerializeToElement("hello") });

        result.IsError.ShouldNotBe(true);
        result.Content.ShouldNotBeEmpty();
        var text = result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().FirstOrDefault()?.Text;
        text.ShouldBe("cold-response");

        agentRuntime.Verify(x => x.RunAsync(It.Is<AgentRunRequest>(r => r.AgentId == agentId), It.IsAny<CancellationToken>()), Times.Once);
    }
}
