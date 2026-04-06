namespace Tnzi.AI.Tests;

public class AgentRunControlToolsTests
{
    [Fact]
    public async Task GetAgentRunAsync_ReturnsCamelCaseJson()
    {
        var runId = Guid.NewGuid();
        var service = new Mock<IAgentRuntimeControlService>();
        service.Setup(x => x.GetStateAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgentRunControlStateDto
            {
                RunId = runId,
                Status = AgentRunStatus.Completed,
                InputSummary = "input"
            }));

        var tools = new AgentRunControlTools(service.Object);

        var json = await tools.GetAgentRunAsync(runId);

        json.ShouldContain("\"runId\"");
        json.ShouldContain("\"status\"");
    }

    [Fact]
    public async Task SpawnAgentAsync_ReturnsSpawnedRunJson()
    {
        var runId = Guid.NewGuid();
        var service = new Mock<IAgentRuntimeControlService>();
        service.Setup(x => x.SpawnAsync(It.IsAny<SpawnAgentRunInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgentRunControlStateDto
            {
                RunId = runId,
                Status = AgentRunStatus.Pending,
                InputSummary = "research this repo"
            }));

        var tools = new AgentRunControlTools(service.Object);

        var json = await tools.SpawnAgentAsync("research this repo", subAgentType: "researcher");

        json.ShouldContain("\"runId\"");
        json.ShouldContain("\"inputSummary\"");
    }

    [Fact]
    public async Task ListAgentRunsAsync_ReturnsSerializedList()
    {
        var runId = Guid.NewGuid();
        var service = new Mock<IAgentRuntimeControlService>();
        service.Setup(x => x.ListRunsAsync(20, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new List<AgentRunListItemDto>
            {
                new()
                {
                    RunId = runId,
                    AgentId = Guid.NewGuid(),
                    Status = AgentRunStatus.Completed,
                    InputSummary = "analyze code",
                    CreationTime = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc)
                }
            }));

        var tools = new AgentRunControlTools(service.Object);

        var json = await tools.ListAgentRunsAsync();

        json.ShouldContain("\"runId\"");
        json.ShouldContain("\"status\"");
        json.ShouldContain("\"inputSummary\"");
        json.ShouldContain("analyze code");
    }

    [Fact]
    public async Task ListAgentRunsAsync_WithStatusFilter_PassesStatusToService()
    {
        var service = new Mock<IAgentRuntimeControlService>();
        service.Setup(x => x.ListRunsAsync(10, AgentRunStatus.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new List<AgentRunListItemDto>()));

        var tools = new AgentRunControlTools(service.Object);

        var json = await tools.ListAgentRunsAsync(maxResults: 10, status: "Running");

        service.Verify(x => x.ListRunsAsync(10, AgentRunStatus.Running, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAgentRunsAsync_InvalidStatus_IgnoresFilter()
    {
        var service = new Mock<IAgentRuntimeControlService>();
        service.Setup(x => x.ListRunsAsync(20, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new List<AgentRunListItemDto>()));

        var tools = new AgentRunControlTools(service.Object);

        var json = await tools.ListAgentRunsAsync(status: "InvalidStatus");

        service.Verify(x => x.ListRunsAsync(20, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAgentRunsAsync_ServiceFails_ReturnsErrorMessage()
    {
        var service = new Mock<IAgentRuntimeControlService>();
        service.Setup(x => x.ListRunsAsync(20, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<List<AgentRunListItemDto>>("Database error"));

        var tools = new AgentRunControlTools(service.Object);

        var result = await tools.ListAgentRunsAsync();

        result.ShouldBe("Database error");
    }

    [Fact]
    public async Task ListSubAgentTypesAsync_ReturnsSerializedTypes()
    {
        var service = new Mock<IAgentRuntimeControlService>();
        service.Setup(x => x.ListSubAgentTypesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new List<SubAgentTypeDto>
            {
                new()
                {
                    Name = "researcher",
                    CapabilityTags = ["research", "web"]
                }
            }));

        var tools = new AgentRunControlTools(service.Object);

        var json = await tools.ListSubAgentTypesAsync();

        json.ShouldContain("researcher");
        json.ShouldContain("capabilityTags");
    }
}
