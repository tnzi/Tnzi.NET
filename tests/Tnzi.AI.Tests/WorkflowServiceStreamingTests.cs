using Microsoft.Extensions.DependencyInjection;

namespace Tnzi.AI.Tests;

/// <summary>
/// WorkflowService 流式执行测试
/// </summary>
public class WorkflowServiceStreamingTests
{
    [Fact]
    public async Task RunStreamingAsync_SequentialMode_StreamsStepThenFinalCompletion()
    {
        var workflowId = Guid.NewGuid();
        var steps = new[]
        {
            new WorkflowStepDto
            {
                StepId = "step-a",
                Order = 1,
                Configuration = new Dictionary<string, string>
                {
                    ["nodeType"] = "fixed-result",
                    ["result"] = "Hello"
                }
            },
            new WorkflowStepDto
            {
                StepId = "step-b",
                Order = 2,
                Configuration = new Dictionary<string, string>
                {
                    ["nodeType"] = "fixed-result",
                    ["result"] = "World"
                }
            }
        };
        var repository = new Mock<IRepository<WorkflowDefinition, Guid>>();
        repository.Setup(x => x.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinition
            {
                Id = workflowId,
                Name = "wf",
                IsEnabled = true,
                ExecutionMode = WorkflowExecutionMode.Sequential,
                Steps = JsonSerializer.Serialize(steps, TnziJsonDefaults.Options)
            });

        var usageLog = new Mock<IUsageLogService>();
        var quota = new Mock<IQuotaService>();
        var service = CreateService(repository, usageLog, quota);

        var results = new List<WorkflowExecutionResultDto>();
        await foreach (var item in service.RunStreamingAsync(workflowId, "input"))
        {
            results.Add(item);
        }

        results.Count.ShouldBe(3);
        results[0].Status.ShouldBe("Step 'step-a'");
        results[0].Output.ShouldBe("Hello");
        results[1].Status.ShouldBe("Step 'step-b'");
        results[1].Output.ShouldBe("World");
        results[2].Status.ShouldBe("Completed");
        var completedStepResults = results[2].StepResults;
        completedStepResults.ShouldNotBeNull();
        completedStepResults!.Count.ShouldBe(2);
        completedStepResults[0].StepId.ShouldBe("step-a");
        completedStepResults[0].Output.ShouldBe("Hello");
        completedStepResults[1].StepId.ShouldBe("step-b");
        completedStepResults[1].Output.ShouldBe("World");
        results[2].Output.ShouldBe("World");
    }

    [Fact]
    public async Task RunStreamingAsync_ParallelMode_EmitsStepEventsAndFinalFailedStatus()
    {
        var workflowId = Guid.NewGuid();
        var steps = new[]
        {
            new WorkflowStepDto
            {
                StepId = "step-a",
                Order = 1,
                Configuration = new Dictionary<string, string>
                {
                    ["nodeType"] = "fixed-result",
                    ["result"] = "ok"
                }
            },
            new WorkflowStepDto
            {
                StepId = "step-b",
                Order = 2,
                Configuration = new Dictionary<string, string>
                {
                    ["nodeType"] = "failing-result",
                    ["error"] = "boom"
                }
            }
        };
        var repository = new Mock<IRepository<WorkflowDefinition, Guid>>();
        repository.Setup(x => x.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinition
            {
                Id = workflowId,
                Name = "wf",
                IsEnabled = true,
                ExecutionMode = WorkflowExecutionMode.Parallel,
                Steps = JsonSerializer.Serialize(steps, TnziJsonDefaults.Options)
            });

        var usageLog = new Mock<IUsageLogService>();
        var quota = new Mock<IQuotaService>();
        var service = CreateService(repository, usageLog, quota);

        var results = new List<WorkflowExecutionResultDto>();
        await foreach (var item in service.RunStreamingAsync(workflowId, "input"))
        {
            results.Add(item);
        }

        results.Count.ShouldBe(3);
        results[0].Status.ShouldBe("Step 'step-a'");
        results[0].Output.ShouldBe("ok");
        results[1].Status.ShouldBe("Step 'step-b'");
        results[1].Output.ShouldContain("[Error: boom]");
        results[2].Status.ShouldBe("Failed");
        results[2].Output.ShouldContain("[Error: boom]");
        results[2].StepResults.ShouldNotBeNull();
        results[2].StepResults!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task RunStreamingAsync_DagMode_UsesWorkflowEngineAndEmitsStepEvents()
    {
        var workflowId = Guid.NewGuid();
        var steps = new[]
        {
            new WorkflowStepDto
            {
                StepId = "step-a",
                Configuration = new Dictionary<string, string>
                {
                    ["nodeType"] = "fixed-result",
                    ["result"] = "A"
                }
            },
            new WorkflowStepDto
            {
                StepId = "step-b",
                DependsOn = ["step-a"],
                Configuration = new Dictionary<string, string>
                {
                    ["nodeType"] = "fixed-result",
                    ["result"] = "B"
                }
            }
        };
        var repository = new Mock<IRepository<WorkflowDefinition, Guid>>();
        repository.Setup(x => x.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinition
            {
                Id = workflowId,
                Name = "wf",
                IsEnabled = true,
                ExecutionMode = WorkflowExecutionMode.Dag,
                Steps = JsonSerializer.Serialize(steps, TnziJsonDefaults.Options)
            });

        var usageLog = new Mock<IUsageLogService>();
        var quota = new Mock<IQuotaService>();
        var service = CreateService(repository, usageLog, quota);

        var results = new List<WorkflowExecutionResultDto>();
        await foreach (var item in service.RunStreamingAsync(workflowId, "input"))
        {
            results.Add(item);
        }

        results.Count.ShouldBe(3);
        results[0].Status.ShouldBe("Step 'step-a'");
        results[0].Output.ShouldBe("A");
        results[1].Status.ShouldBe("Step 'step-b'");
        results[1].Output.ShouldBe("B");
        results[2].Status.ShouldBe("Completed");
        results[2].Output.ShouldBe("B");
    }

    private static WorkflowService CreateService(
        Mock<IRepository<WorkflowDefinition, Guid>> repository,
        Mock<IUsageLogService> usageLog,
        Mock<IQuotaService> quota)
    {
        var checkpointStore = new Mock<IWorkflowCheckpointStore>();
        var executionRepository = new Mock<IRepository<WorkflowExecution, Guid>>();
        var runRepository = new Mock<IRepository<AgentRun, Guid>>();
        runRepository.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<AgentRun, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRun?)null);
        usageLog.Setup(x => x.LogUsageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IWorkflowNode, AgentNode>();
        services.AddScoped<IWorkflowNode, FixedResultNode>();
        services.AddScoped<IWorkflowNode, FailingResultNode>();
        services.AddScoped<WorkflowNodeExecutor>();
        services.AddScoped<WorkflowEngine>();
        var serviceProvider = services.BuildServiceProvider();

        return new WorkflowService(
            repository.Object,
            executionRepository.Object,
            runRepository.Object,
            usageLog.Object,
            quota.Object,
            checkpointStore.Object,
            serviceProvider.GetRequiredService<WorkflowEngine>(),
            serviceProvider);
    }

    private sealed class FixedResultNode : IWorkflowNode
    {
        public string NodeType => "fixed-result";

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkflowNodeResult
            {
                Output = context.Step.Configuration?.GetValueOrDefault("result") ?? string.Empty
            });
        }
    }

    private sealed class FailingResultNode : IWorkflowNode
    {
        public string NodeType => "failing-result";

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            var error = context.Step.Configuration?.GetValueOrDefault("error") ?? "failure";
            return Task.FromResult(new WorkflowNodeResult
            {
                IsSuccess = false,
                Error = error,
                Output = $"[Error: {error}]"
            });
        }
    }
}
