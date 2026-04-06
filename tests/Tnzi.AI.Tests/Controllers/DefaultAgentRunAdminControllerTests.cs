using Tnzi.AI.Controllers.Admin;

namespace Tnzi.AI.Tests.Controllers;

public class DefaultAgentRunAdminControllerTests
{
    private readonly Mock<IAgentRunService> _runService = new();
    private readonly Mock<IAgentTraceService> _traceService = new();
    private readonly Mock<IAgentRuntimeControlService> _runtimeControlService = new();
    private readonly DefaultAgentRunAdminController _controller;

    public DefaultAgentRunAdminControllerTests()
    {
        _controller = new DefaultAgentRunAdminController(
            _runService.Object,
            _traceService.Object,
            _runtimeControlService.Object);
    }

    [Fact]
    public async Task GetState_DelegatesToRuntimeControlService()
    {
        var runId = Guid.NewGuid();
        _runtimeControlService.Setup(x => x.GetStateAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgentRunControlStateDto
            {
                RunId = runId,
                Status = AgentRunStatus.Completed,
                InputSummary = "done"
            }));

        var result = await _controller.GetState(runId);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.RunId.ShouldBe(runId);
    }

    [Fact]
    public async Task Spawn_DelegatesToRuntimeControlService()
    {
        var runId = Guid.NewGuid();
        _runtimeControlService.Setup(x => x.SpawnAsync(It.IsAny<SpawnAgentRunInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgentRunControlStateDto
            {
                RunId = runId,
                Status = AgentRunStatus.Pending,
                InputSummary = "background task"
            }));

        var result = await _controller.Spawn(new SpawnAgentRunInput
        {
            Message = "background task",
            SubAgentType = "researcher"
        });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.RunId.ShouldBe(runId);
    }

    [Fact]
    public async Task Wait_DelegatesToRuntimeControlService()
    {
        var runId = Guid.NewGuid();
        _runtimeControlService.Setup(x => x.WaitAsync(runId, It.IsAny<WaitAgentRunInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgentRunWaitResultDto
            {
                State = new AgentRunControlStateDto
                {
                    RunId = runId,
                    Status = AgentRunStatus.Completed,
                    InputSummary = "done"
                },
                PollCount = 2
            }));

        var result = await _controller.Wait(runId, new WaitAgentRunInput { TimeoutSeconds = 10 });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.PollCount.ShouldBe(2);
    }

    [Fact]
    public async Task SendInput_DelegatesToRuntimeControlService()
    {
        var runId = Guid.NewGuid();
        _runtimeControlService.Setup(x => x.SendInputAsync(runId, It.IsAny<SendAgentRunInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgentRunControlStateDto
            {
                RunId = runId,
                Status = AgentRunStatus.RequiresClarification,
                InputSummary = "need more input"
            }));

        var result = await _controller.SendInput(runId, new SendAgentRunInput
        {
            Message = "continue"
        });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Status.ShouldBe(AgentRunStatus.RequiresClarification);
    }

    [Fact]
    public async Task GetSubAgentTypes_DelegatesToRuntimeControlService()
    {
        _runtimeControlService.Setup(x => x.ListSubAgentTypesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new List<SubAgentTypeDto>
            {
                new() { Name = "researcher", Description = "Research tasks" }
            }));

        var result = await _controller.GetSubAgentTypes();

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Count.ShouldBe(1);
        result.Data[0].Name.ShouldBe("researcher");
    }
}
