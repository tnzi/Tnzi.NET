
namespace Tnzi.AI.Tests;

public class AgentRuntimeControlServiceTests
{
    [Fact]
    public async Task GetStateAsync_WorkflowRun_EnrichesWorkflowStatusAndInterrupt()
    {
        var runId = Guid.NewGuid();
        var executionId = "wf-exec-1";

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.GetWithNodesAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRun
            {
                Id = runId,
                Status = AgentRunStatus.Running,
                ExecutionMode = AgentExecutionMode.Single,
                WorkflowDefinitionId = Guid.NewGuid(),
                WorkflowExecutionId = executionId,
                InputSummary = "input",
                Nodes =
                [
                    new AgentRunNode
                    {
                        Id = Guid.NewGuid(),
                        RunId = runId,
                        NodeName = "approve_budget",
                        Status = AgentRunNodeStatus.AwaitingApproval,
                        OrderIndex = 1
                    }
                ]
            });

        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.GetExecutionStatusAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new WorkflowExecutionStatusDto
            {
                ExecutionId = executionId,
                Status = "AwaitingInput",
                StepsAwaitingApproval = ["approve_budget"]
            }));
        workflowService.Setup(x => x.GetPendingInterruptAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new WorkflowInterruptDto
            {
                ExecutionId = executionId,
                StepId = "ask_user",
                Type = "HumanInput",
                Reason = "Need deployment target"
            }));

        var service = CreateService(
            runStore: runStore.Object,
            workflowService: workflowService.Object);

        var result = await service.GetStateAsync(runId);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Status.ShouldBe(AgentRunStatus.RequiresClarification);
        result.Data.WorkflowStatus.ShouldBe("AwaitingInput");
        result.Data.CanResume.ShouldBeTrue();
        result.Data.CanCancel.ShouldBeTrue();
        result.Data.CanSendInput.ShouldBeTrue();
        result.Data.PendingInterrupt.ShouldNotBeNull();
        result.Data.PendingInterrupt.StepId.ShouldBe("ask_user");
        result.Data.AwaitingApprovalNodeNames.ShouldContain("approve_budget");
    }

    [Fact]
    public async Task WaitAsync_PollsUntilObservableTerminal()
    {
        var runId = Guid.NewGuid();
        var queue = new Queue<AgentRun>([
            new AgentRun
            {
                Id = runId,
                Status = AgentRunStatus.Running,
                ExecutionMode = AgentExecutionMode.Single,
                InputSummary = "step 1"
            },
            new AgentRun
            {
                Id = runId,
                Status = AgentRunStatus.Completed,
                ExecutionMode = AgentExecutionMode.Single,
                InputSummary = "step 1",
                OutputSummary = "done"
            }
        ]);

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.GetWithNodesAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => queue.Count > 1 ? queue.Dequeue() : queue.Peek());

        var service = CreateService(runStore: runStore.Object);

        var result = await service.WaitAsync(runId, new WaitAgentRunInput
        {
            TimeoutSeconds = 5,
            PollIntervalMs = 100
        });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.TimedOut.ShouldBeFalse();
        result.Data.State.Status.ShouldBe(AgentRunStatus.Completed);
        result.Data.PollCount.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task WaitAsync_WorkflowAwaitingApproval_StopsImmediatelyAsObservableState()
    {
        var runId = Guid.NewGuid();
        var executionId = "wf-await-approval";

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.GetWithNodesAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRun
            {
                Id = runId,
                Status = AgentRunStatus.Running,
                ExecutionMode = AgentExecutionMode.Single,
                WorkflowDefinitionId = Guid.NewGuid(),
                WorkflowExecutionId = executionId,
                InputSummary = "waiting"
            });

        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.GetExecutionStatusAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new WorkflowExecutionStatusDto
            {
                ExecutionId = executionId,
                Status = "AwaitingApproval",
                StepsAwaitingApproval = ["approve_release"]
            }));
        workflowService.Setup(x => x.GetPendingInterruptAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<WorkflowInterruptDto>("No interrupt", 404));

        var service = CreateService(
            runStore: runStore.Object,
            workflowService: workflowService.Object);

        var result = await service.WaitAsync(runId, new WaitAgentRunInput
        {
            TimeoutSeconds = 5,
            PollIntervalMs = 100
        });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.TimedOut.ShouldBeFalse();
        result.Data.State.Status.ShouldBe(AgentRunStatus.AwaitingApproval);
        result.Data.State.AwaitingApprovalNodeNames.ShouldContain("approve_release");
        result.Data.PollCount.ShouldBe(1);
    }

    [Fact]
    public async Task SendInputAsync_DelegatesToResumeAndReturnsUpdatedState()
    {
        var runId = Guid.NewGuid();

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.GetWithNodesAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRun
            {
                Id = runId,
                Status = AgentRunStatus.Completed,
                ExecutionMode = AgentExecutionMode.Single,
                InputSummary = "original",
                OutputSummary = "updated"
            });

        var dispatcher = new Mock<IAgentRunSignalDispatcher>();
        dispatcher.Setup(x => x.DispatchInputAsync(
                runId,
                It.IsAny<SendAgentRunInput>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var service = CreateService(
            runStore: runStore.Object,
            signalDispatcher: dispatcher.Object);

        var result = await service.SendInputAsync(runId, new SendAgentRunInput
        {
            Message = "continue with prod"
        });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.OutputSummary.ShouldBe("updated");
        dispatcher.Verify(x => x.DispatchInputAsync(
            runId,
            It.Is<SendAgentRunInput>(input => input.Message == "continue with prod"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SpawnAsync_DelegatesToSubAgentExecutionService()
    {
        var runId = Guid.NewGuid();
        var subAgentExecution = new Mock<ISubAgentExecutionService>();
        subAgentExecution.Setup(x => x.SpawnAsync(
                It.IsAny<SpawnAgentRunInput>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgentRunControlStateDto
            {
                RunId = runId,
                Status = AgentRunStatus.Pending,
                InputSummary = "delegate this task"
            }));

        var service = CreateService(subAgentExecutionService: subAgentExecution.Object);

        var result = await service.SpawnAsync(new SpawnAgentRunInput
        {
            Message = "delegate this task",
            SubAgentType = "researcher"
        });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.RunId.ShouldBe(runId);
        subAgentExecution.Verify(x => x.SpawnAsync(
            It.Is<SpawnAgentRunInput>(input => input.SubAgentType == "researcher" && input.Message == "delegate this task"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListSubAgentTypesAsync_MapsExtendedMetadata()
    {
        var registry = new Mock<ISubAgentRegistry>();
        registry.Setup(x => x.GetAll()).Returns([
            new SubAgentTypeDefinition(
                Name: "reviewer",
                Description: "Review code",
                ToolGroups: ["file"],
                ExcludedToolGroups: ["task"],
                MaxTurns: 12,
                Instructions: "Review changes carefully.",
                DefaultModel: "gpt-5.4",
                DefaultApprovalMode: ToolApprovalMode.Specific,
                CapabilityTags: ["review", "quality"])
        ]);

        var service = CreateService(subAgentRegistry: registry.Object);

        var result = await service.ListSubAgentTypesAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Count.ShouldBe(1);
        result.Data[0].DefaultModel.ShouldBe("gpt-5.4");
        result.Data[0].DefaultApprovalMode.ShouldBe(ToolApprovalMode.Specific);
        result.Data[0].CapabilityTags!.ShouldContain("quality");
    }

    [Fact]
    public async Task ListRunsAsync_ReturnsItems()
    {
        var runId = Guid.NewGuid();
        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.ListAsync(null, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new AgentRun
                {
                    Id = runId,
                    AgentId = Guid.NewGuid(),
                    Status = AgentRunStatus.Completed,
                    InputSummary = "test input",
                    CreationTime = DateTime.UtcNow
                }
            ]);

        var service = CreateService(runStore: runStore.Object);

        var result = await service.ListRunsAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Count.ShouldBe(1);
        result.Data[0].RunId.ShouldBe(runId);
        result.Data[0].InputSummary.ShouldBe("test input");
    }

    [Fact]
    public async Task ListRunsAsync_WithStatusFilter_PassesFilterToStore()
    {
        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.ListAsync(AgentRunStatus.Running, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = CreateService(runStore: runStore.Object);

        var result = await service.ListRunsAsync(maxResults: 10, status: AgentRunStatus.Running);

        result.Succeeded.ShouldBeTrue();
        runStore.Verify(x => x.ListAsync(AgentRunStatus.Running, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListRunsAsync_ClampsMaxResults()
    {
        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.ListAsync(null, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = CreateService(runStore: runStore.Object);

        // maxResults=200 should be clamped to 100
        await service.ListRunsAsync(maxResults: 200);

        runStore.Verify(x => x.ListAsync(null, 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AgentRuntimeControlService CreateService(
        ISubAgentExecutionService? subAgentExecutionService = null,
        IAgentRunSignalDispatcher? signalDispatcher = null,
        IRunStore? runStore = null,
        IWorkflowService? workflowService = null,
        ISubAgentRegistry? subAgentRegistry = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        return new AgentRuntimeControlService(
            subAgentExecutionService ?? Mock.Of<ISubAgentExecutionService>(),
            signalDispatcher ?? Mock.Of<IAgentRunSignalDispatcher>(),
            runStore ?? Mock.Of<IRunStore>(),
            workflowService ?? Mock.Of<IWorkflowService>(),
            subAgentRegistry ?? Mock.Of<ISubAgentRegistry>(),
            services.BuildServiceProvider());
    }
}
