using Microsoft.Extensions.DependencyInjection;

namespace Tnzi.AI.Tests;

public class WorkflowEngineRunTrackingTests
{
    [Fact]
    public async Task ExecuteAsync_ApprovalWorkflow_CreatesTrackedRun_AndMarksAwaitingApproval()
    {
        var workflowDefinitionId = Guid.NewGuid();
        var createdRuns = new List<AgentRun>();
        var createdNodes = new List<AgentRunNode>();
        var updatedNodes = new List<AgentRunNode>();

        var runStore = CreateRunStore(createdRuns, createdNodes, updatedNodes);
        var checkpointStore = new Mock<IWorkflowCheckpointStore>();
        checkpointStore.Setup(x => x.SaveCheckpointAsync(It.IsAny<WorkflowCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(runStore.Object);
        services.AddScoped<IWorkflowNode, ApprovalNode>();
        services.AddScoped<WorkflowNodeExecutor>();
        var serviceProvider = services.BuildServiceProvider();

        var engine = new WorkflowEngine(Mock.Of<ILogger<WorkflowEngine>>());

        var graph = new WorkflowGraph([
            new WorkflowStepDto
            {
                StepId = "approval-step",
                Configuration = new Dictionary<string, string>
                {
                    ["nodeType"] = "approval"
                }
            }
        ]);

        var result = await engine.ExecuteAsync(
            graph,
            "Review this content",
            serviceProvider,
            new WorkflowExecutionOptions
            {
                ExecutionId = "workflow-run-001",
                WorkflowDefinitionId = workflowDefinitionId,
                CheckpointStore = checkpointStore.Object
            });

        result.AwaitingApproval.ShouldBeTrue();
        result.AwaitingApprovalStepId.ShouldBe("approval-step");
        result.RunId.ShouldNotBeNull();

        createdRuns.Count.ShouldBe(1);
        createdRuns[0].WorkflowDefinitionId.ShouldBe(workflowDefinitionId);
        createdRuns[0].WorkflowExecutionId.ShouldBe("workflow-run-001");
        createdRuns[0].Status.ShouldBe(AgentRunStatus.AwaitingApproval);

        createdNodes.ShouldContain(n => n.NodeName == "approval-step");
        updatedNodes.ShouldContain(n => n.NodeName == "approval-step" && n.Status == AgentRunNodeStatus.AwaitingApproval);
    }

    [Fact]
    public async Task ExecuteAsync_ConditionalRoute_PreservesSharedJoinNode()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IWorkflowNode, RouteNode>();
        services.AddScoped<IWorkflowNode, StepANode>();
        services.AddScoped<IWorkflowNode, StepBNode>();
        services.AddScoped<IWorkflowNode, JoinNode>();
        services.AddScoped<WorkflowNodeExecutor>();
        var serviceProvider = services.BuildServiceProvider();

        var engine = new WorkflowEngine(Mock.Of<ILogger<WorkflowEngine>>());

        var graph = new WorkflowGraph(
        [
            new WorkflowStepDto
            {
                StepId = "router",
                Configuration = new Dictionary<string, string> { ["nodeType"] = "route-test" }
            },
            new WorkflowStepDto
            {
                StepId = "step-a",
                DependsOn = ["router"],
                Configuration = new Dictionary<string, string> { ["nodeType"] = "step-a-test" }
            },
            new WorkflowStepDto
            {
                StepId = "step-b",
                DependsOn = ["router"],
                Configuration = new Dictionary<string, string> { ["nodeType"] = "step-b-test" }
            },
            new WorkflowStepDto
            {
                StepId = "join",
                DependsOn = ["step-a", "step-b"],
                Configuration = new Dictionary<string, string> { ["nodeType"] = "join-test" }
            }
        ],
        [
            new ConditionalEdge
            {
                FromNodeId = "router",
                ConditionType = EdgeConditionType.OutputContains,
                Routes = new Dictionary<string, string>
                {
                    ["go-a"] = "step-a",
                    ["go-b"] = "step-b"
                }
            }
        ]);

        var result = await engine.ExecuteAsync(graph, "input", serviceProvider);

        result.FinalOutput.ShouldBe("joined:from-a");
        result.StepResults.ShouldContain(x => x.StepId == "join" && x.Output == "joined:from-a");
    }

    [Fact]
    public async Task ExecuteAsync_NodeFails_StopsDownstreamAndMarksRunFailed()
    {
        var createdRuns = new List<AgentRun>();
        var createdNodes = new List<AgentRunNode>();
        var updatedNodes = new List<AgentRunNode>();

        var runStore = CreateRunStore(createdRuns, createdNodes, updatedNodes);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(runStore.Object);
        services.AddScoped<IWorkflowNode, FailingNode>();
        services.AddScoped<IWorkflowNode, DownstreamNode>();
        services.AddScoped<WorkflowNodeExecutor>();
        var serviceProvider = services.BuildServiceProvider();

        var engine = new WorkflowEngine(Mock.Of<ILogger<WorkflowEngine>>());

        var graph = new WorkflowGraph(
        [
            new WorkflowStepDto
            {
                StepId = "fail",
                Configuration = new Dictionary<string, string> { ["nodeType"] = "fail-test" }
            },
            new WorkflowStepDto
            {
                StepId = "downstream",
                DependsOn = ["fail"],
                Configuration = new Dictionary<string, string> { ["nodeType"] = "downstream-test" }
            }
        ]);

        var result = await engine.ExecuteAsync(
            graph,
            "input",
            serviceProvider,
            new WorkflowExecutionOptions
            {
                ExecutionId = "workflow-run-failed",
                CheckpointStore = Mock.Of<IWorkflowCheckpointStore>()
            });

        result.HasFailure.ShouldBeTrue();
        result.FinalOutput.ShouldBe("[Error: boom]");
        result.StepResults.ShouldContain(x => x.StepId == "fail");
        result.StepResults.ShouldNotContain(x => x.StepId == "downstream");

        createdRuns.Count.ShouldBe(1);
        createdRuns[0].Status.ShouldBe(AgentRunStatus.Failed);
        createdNodes.ShouldContain(n => n.NodeName == "fail");
        createdNodes.ShouldNotContain(n => n.NodeName == "downstream");
    }

    [Fact]
    public async Task ExecuteAsync_CancelSignal_StopsBeforeNextReadyLayer()
    {
        var createdRuns = new List<AgentRun>();
        var createdNodes = new List<AgentRunNode>();
        var updatedNodes = new List<AgentRunNode>();

        var runStore = CreateRunStore(createdRuns, createdNodes, updatedNodes);
        var checkpointStore = new Mock<IWorkflowCheckpointStore>();
        checkpointStore.Setup(x => x.SaveCheckpointAsync(It.IsAny<WorkflowCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mailbox = new Mock<IWorkflowExecutionMailbox>();
        mailbox.SetupSequence(x => x.GetPendingSignalsAsync("workflow-run-cancel", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([
                new WorkflowExecutionSignal
                {
                    SignalId = "cancel-1",
                    Type = WorkflowExecutionSignalTypes.Cancel
                }
            ]);
        mailbox.Setup(x => x.AcknowledgeSignalsAsync("workflow-run-cancel", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(runStore.Object);
        services.AddSingleton(mailbox.Object);
        services.AddScoped<IWorkflowNode, FixedNode>();
        services.AddScoped<WorkflowNodeExecutor>();
        var serviceProvider = services.BuildServiceProvider();

        var engine = new WorkflowEngine(Mock.Of<ILogger<WorkflowEngine>>());

        var graph = new WorkflowGraph(
        [
            new WorkflowStepDto
            {
                StepId = "step-1",
                Configuration = new Dictionary<string, string> { ["nodeType"] = "fixed-test" }
            },
            new WorkflowStepDto
            {
                StepId = "step-2",
                DependsOn = ["step-1"],
                Configuration = new Dictionary<string, string> { ["nodeType"] = "fixed-test" }
            }
        ]);

        var result = await engine.ExecuteAsync(
            graph,
            "input",
            serviceProvider,
            new WorkflowExecutionOptions
            {
                ExecutionId = "workflow-run-cancel",
                CheckpointStore = checkpointStore.Object
            });

        result.Cancelled.ShouldBeTrue();
        result.StatusText.ShouldBe("Cancelled");
        result.StepResults.ShouldContain(x => x.StepId == "step-1");
        result.StepResults.ShouldNotContain(x => x.StepId == "step-2");
        createdRuns.Count.ShouldBe(1);
        createdRuns[0].Status.ShouldBe(AgentRunStatus.Cancelled);
    }

    private static Mock<IRunStore> CreateRunStore(
        List<AgentRun> createdRuns,
        List<AgentRunNode> createdNodes,
        List<AgentRunNode> updatedNodes)
    {
        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRun run, CancellationToken _) =>
            {
                run.Id = Guid.NewGuid();
                createdRuns.Add(run);
                return run;
            });
        runStore.Setup(x => x.GetWithNodesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, CancellationToken _) => createdRuns.FirstOrDefault());
        runStore.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        runStore.Setup(x => x.AddNodeAsync(It.IsAny<AgentRunNode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRunNode node, CancellationToken _) =>
            {
                node.Id = Guid.NewGuid();
                createdNodes.Add(node);
                return node;
            });
        runStore.Setup(x => x.UpdateNodeAsync(It.IsAny<AgentRunNode>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRunNode, CancellationToken>((node, _) => updatedNodes.Add(node))
            .Returns(Task.CompletedTask);
        return runStore;
    }

    private sealed class RouteNode : IWorkflowNode
    {
        public string NodeType => "route-test";

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkflowNodeResult
            {
                Output = "go-a",
                RouteTo = "step-a"
            });
        }
    }

    private sealed class StepANode : IWorkflowNode
    {
        public string NodeType => "step-a-test";

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkflowNodeResult { Output = "from-a" });
        }
    }

    private sealed class StepBNode : IWorkflowNode
    {
        public string NodeType => "step-b-test";

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkflowNodeResult { Output = "from-b" });
        }
    }

    private sealed class JoinNode : IWorkflowNode
    {
        public string NodeType => "join-test";

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            var text = context.DependencyOutputs
                .Select(x => x.Value.Text)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                ?? string.Empty;

            return Task.FromResult(new WorkflowNodeResult
            {
                Output = $"joined:{text}"
            });
        }
    }

    private sealed class FailingNode : IWorkflowNode
    {
        public string NodeType => "fail-test";

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkflowNodeResult
            {
                IsSuccess = false,
                Error = "boom",
                Output = "[Error: boom]"
            });
        }
    }

    private sealed class DownstreamNode : IWorkflowNode
    {
        public string NodeType => "downstream-test";

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkflowNodeResult { Output = "should-not-run" });
        }
    }

    private sealed class FixedNode : IWorkflowNode
    {
        public string NodeType => "fixed-test";

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkflowNodeResult
            {
                Output = $"{context.Step.StepId}-done"
            });
        }
    }
}
