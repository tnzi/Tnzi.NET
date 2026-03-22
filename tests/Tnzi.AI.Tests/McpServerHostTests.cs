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
                Transport = "sse",
                ExposedAgentIds = [agentId]
            }),
            NullLogger<McpServerSecurityMiddleware>.Instance,
            serviceProvider);

        var host = new McpServerHost(
            serviceProvider,
            MsOptions.Create(new McpServerOptions
            {
                Enabled = true,
                Transport = "sse",
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
}
