using Microsoft.Extensions.DependencyInjection;

namespace Tnzi.AI.Tests;

/// <summary>
/// WorkflowService 检查点集成测试
/// </summary>
public class WorkflowServiceCheckpointIntegrationTests
{
    [Fact]
    public async Task RunAsync_DagMode_PersistsExecutionRecord_AndEnablesCheckpointing()
    {
        var workflowId = Guid.NewGuid();
        var insertedExecutions = new List<WorkflowExecution>();
        var createdRuns = new List<AgentRun>();

        var workflowRepository = new Mock<IRepository<WorkflowDefinition, Guid>>();
        workflowRepository.Setup(x => x.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinition
            {
                Id = workflowId,
                Name = "wf",
                IsEnabled = true,
                ExecutionMode = WorkflowExecutionMode.Dag,
                Steps = SerializeSteps(
                    new WorkflowStepDto
                    {
                        StepId = "step-a",
                        Configuration = new Dictionary<string, string>
                        {
                            ["nodeType"] = "fixed-result",
                            ["result"] = "Result A"
                        }
                    })
            });

        var executionRepository = new Mock<IRepository<WorkflowExecution, Guid>>();
        executionRepository.Setup(x => x.InsertAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowExecution, CancellationToken>((execution, _) => insertedExecutions.Add(execution))
            .Returns(Task.CompletedTask);

        var checkpointStore = new Mock<IWorkflowCheckpointStore>();
        checkpointStore.Setup(x => x.SaveCheckpointAsync(It.IsAny<WorkflowCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRun run, CancellationToken _) =>
            {
                createdRuns.Add(run);
                return run;
            });
        runStore.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        runStore.Setup(x => x.AddNodeAsync(It.IsAny<AgentRunNode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRunNode node, CancellationToken _) => node);
        runStore.Setup(x => x.UpdateNodeAsync(It.IsAny<AgentRunNode>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(workflowRepository, executionRepository, checkpointStore, runStore);

        var result = await service.RunAsync(workflowId, "input");

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.ExecutionId.ShouldNotBeNullOrWhiteSpace();
        result.Data.RunId.ShouldNotBeNull();

        insertedExecutions.Count.ShouldBe(1);
        insertedExecutions[0].WorkflowDefinitionId.ShouldBe(workflowId);
        insertedExecutions[0].ExecutionId.ShouldBe(result.Data.ExecutionId);
        createdRuns.Count.ShouldBe(1);
        result.Data.RunId.ShouldBe(createdRuns[0].Id);

        checkpointStore.Verify(x => x.SaveCheckpointAsync(It.IsAny<WorkflowCheckpoint>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task RunAsync_DagMode_WhenDagValidationFails_MarksExecutionAsFailed()
    {
        var workflowId = Guid.NewGuid();
        WorkflowExecution? insertedExecution = null;

        var workflowRepository = new Mock<IRepository<WorkflowDefinition, Guid>>();
        workflowRepository.Setup(x => x.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinition
            {
                Id = workflowId,
                Name = "wf",
                IsEnabled = true,
                ExecutionMode = WorkflowExecutionMode.Dag,
                Steps = SerializeSteps(
                    new WorkflowStepDto
                    {
                        StepId = "step-a",
                        DependsOn = ["missing-step"]
                    })
            });

        var executionRepository = new Mock<IRepository<WorkflowExecution, Guid>>();
        executionRepository.Setup(x => x.InsertAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowExecution, CancellationToken>((execution, _) => insertedExecution = execution)
            .Returns(Task.CompletedTask);
        executionRepository.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<WorkflowExecution, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => insertedExecution);
        executionRepository.Setup(x => x.UpdateAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var checkpointStore = new Mock<IWorkflowCheckpointStore>();
        var service = CreateService(workflowRepository, executionRepository, checkpointStore);

        var result = await service.RunAsync(workflowId, "input");

        result.Succeeded.ShouldBeFalse();
        insertedExecution.ShouldNotBeNull();
        insertedExecution!.Status.ShouldBe(WorkflowExecutionStatus.Failed);
        insertedExecution.CompletedTime.ShouldNotBeNull();
    }

    [Fact]
    public async Task ResumeAsync_UsesWorkflowExecutionRecord_ToResolveWorkflowDefinition()
    {
        var workflowId = Guid.NewGuid();
        const string executionId = "resume-exec-001";

        var workflowRepository = new Mock<IRepository<WorkflowDefinition, Guid>>();
        workflowRepository.Setup(x => x.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinition
            {
                Id = workflowId,
                Name = "wf",
                IsEnabled = true,
                ExecutionMode = WorkflowExecutionMode.Dag,
                Steps = SerializeSteps(
                    new WorkflowStepDto
                    {
                        StepId = "step-a",
                        Configuration = new Dictionary<string, string>
                        {
                            ["nodeType"] = "fixed-result",
                            ["result"] = "Result A"
                        }
                    })
            });

        WorkflowExecution? casUpdatedEntity = null;
        var executionRepository = new Mock<IRepository<WorkflowExecution, Guid>>();
        executionRepository.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<WorkflowExecution, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowExecution
            {
                ExecutionId = executionId,
                WorkflowDefinitionId = workflowId,
                InitialInput = "input"
            });
        executionRepository.Setup(x => x.UpdateAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowExecution, CancellationToken>((e, _) => casUpdatedEntity ??= e)
            .Returns(Task.CompletedTask);

        var checkpointStore = new Mock<IWorkflowCheckpointStore>();
        checkpointStore.Setup(x => x.GetCheckpointAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowCheckpoint
            {
                ExecutionId = executionId,
                InitialInput = "input",
                Status = WorkflowExecutionStatus.Paused,
                CompletedStepIds = [],
                StepOutputs = new Dictionary<string, WorkflowStepOutput>()
            });
        checkpointStore.Setup(x => x.SaveCheckpointAsync(It.IsAny<WorkflowCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(workflowRepository, executionRepository, checkpointStore);

        var result = await service.ResumeAsync(executionId);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.ExecutionId.ShouldBe(executionId);
        result.Data.Output.ShouldBe("Result A");

        // CAS guard: the execution is flipped to Running via UpdateAsync BEFORE the engine runs.
        executionRepository.Verify(x => x.UpdateAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        casUpdatedEntity.ShouldNotBeNull();
        casUpdatedEntity!.Status.ShouldBe(WorkflowExecutionStatus.Running);
    }

    [Fact]
    public async Task ResumeAsync_ConcurrentResume_LosingWriterGets409_WithoutRunningEngine()
    {
        var workflowId = Guid.NewGuid();
        const string executionId = "resume-cas-conflict-001";

        var workflowRepository = new Mock<IRepository<WorkflowDefinition, Guid>>();
        workflowRepository.Setup(x => x.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinition
            {
                Id = workflowId,
                Name = "wf",
                IsEnabled = true,
                ExecutionMode = WorkflowExecutionMode.Dag,
                Steps = SerializeSteps(
                    new WorkflowStepDto
                    {
                        StepId = "step-a",
                        Configuration = new Dictionary<string, string>
                        {
                            ["nodeType"] = "fixed-result",
                            ["result"] = "Result A"
                        }
                    })
            });

        var executionRepository = new Mock<IRepository<WorkflowExecution, Guid>>();
        executionRepository.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<WorkflowExecution, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowExecution
            {
                ExecutionId = executionId,
                WorkflowDefinitionId = workflowId,
                InitialInput = "input"
            });
        // Simulate losing the optimistic-concurrency CAS: a concurrent Resume already
        // bumped the stamp, so this writer's pre-engine UpdateAsync throws.
        executionRepository.Setup(x => x.UpdateAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("stale concurrency token"));

        var checkpointStore = new Mock<IWorkflowCheckpointStore>();
        checkpointStore.Setup(x => x.GetCheckpointAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowCheckpoint
            {
                ExecutionId = executionId,
                InitialInput = "input",
                Status = WorkflowExecutionStatus.Paused,
                CompletedStepIds = [],
                StepOutputs = new Dictionary<string, WorkflowStepOutput>()
            });

        var service = CreateService(workflowRepository, executionRepository, checkpointStore);

        var result = await service.ResumeAsync(executionId);

        // The losing writer gets 409 Conflict - the engine never ran (no checkpoint save).
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        checkpointStore.Verify(
            x => x.SaveCheckpointAsync(It.IsAny<WorkflowCheckpoint>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResumeAsync_FailedCheckpoint_AllowsRetryFromCheckpoint()
    {
        var workflowId = Guid.NewGuid();
        const string executionId = "resume-failed-001";

        var workflowRepository = new Mock<IRepository<WorkflowDefinition, Guid>>();
        workflowRepository.Setup(x => x.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinition
            {
                Id = workflowId,
                Name = "wf",
                IsEnabled = true,
                ExecutionMode = WorkflowExecutionMode.Dag,
                Steps = SerializeSteps(
                    new WorkflowStepDto
                    {
                        StepId = "step-a",
                        Configuration = new Dictionary<string, string>
                        {
                            ["nodeType"] = "fixed-result",
                            ["result"] = "Recovered"
                        }
                    })
            });

        var executionRepository = new Mock<IRepository<WorkflowExecution, Guid>>();
        executionRepository.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<WorkflowExecution, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowExecution
            {
                ExecutionId = executionId,
                WorkflowDefinitionId = workflowId,
                InitialInput = "input"
            });
        executionRepository.Setup(x => x.UpdateAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var checkpointStore = new Mock<IWorkflowCheckpointStore>();
        checkpointStore.Setup(x => x.GetCheckpointAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowCheckpoint
            {
                ExecutionId = executionId,
                InitialInput = "input",
                Status = WorkflowExecutionStatus.Failed,
                CompletedStepIds = [],
                StepOutputs = new Dictionary<string, WorkflowStepOutput>()
            });
        checkpointStore.Setup(x => x.SaveCheckpointAsync(It.IsAny<WorkflowCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(workflowRepository, executionRepository, checkpointStore);

        var result = await service.ResumeAsync(executionId);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Output.ShouldBe("Recovered");

        // CAS guard transitions to Running before the engine resumes from the failed checkpoint.
        executionRepository.Verify(x => x.UpdateAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task RunAsync_DagMode_WhenNodeFails_ReturnsFailedStatus()
    {
        var workflowId = Guid.NewGuid();
        WorkflowExecution? insertedExecution = null;

        var workflowRepository = new Mock<IRepository<WorkflowDefinition, Guid>>();
        workflowRepository.Setup(x => x.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinition
            {
                Id = workflowId,
                Name = "wf",
                IsEnabled = true,
                ExecutionMode = WorkflowExecutionMode.Dag,
                Steps = SerializeSteps(
                    new WorkflowStepDto
                    {
                        StepId = "step-a",
                        Configuration = new Dictionary<string, string>
                        {
                            ["nodeType"] = "failing-result"
                        }
                    },
                    new WorkflowStepDto
                    {
                        StepId = "step-b",
                        DependsOn = ["step-a"],
                        Configuration = new Dictionary<string, string>
                        {
                            ["nodeType"] = "fixed-result",
                            ["result"] = "Should not run"
                        }
                    })
            });

        var executionRepository = new Mock<IRepository<WorkflowExecution, Guid>>();
        executionRepository.Setup(x => x.InsertAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowExecution, CancellationToken>((execution, _) => insertedExecution = execution)
            .Returns(Task.CompletedTask);
        executionRepository.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<WorkflowExecution, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => insertedExecution);
        executionRepository.Setup(x => x.UpdateAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var checkpointStore = new Mock<IWorkflowCheckpointStore>();
        checkpointStore.Setup(x => x.SaveCheckpointAsync(It.IsAny<WorkflowCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(workflowRepository, executionRepository, checkpointStore);

        var result = await service.RunAsync(workflowId, "input");

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        var data = result.Data!;
        data.Status.ShouldBe("Failed");
        data.StepResults.ShouldNotBeNull();
        data.StepResults!.ShouldContain(x => x.StepId == "step-a");
        data.StepResults.ShouldNotContain(x => x.StepId == "step-b");
        insertedExecution.ShouldNotBeNull();
        insertedExecution!.Status.ShouldBe(WorkflowExecutionStatus.Failed);
    }

    [Fact]
    public async Task GetCheckpointAsync_UsesEntityUpdatedTime()
    {
        var updatedTime = DateTime.UtcNow.AddMinutes(-1);
        var repository = new Mock<IRepository<WorkflowExecution, Guid>>();
        repository.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<WorkflowExecution, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowExecution
            {
                ExecutionId = "exec-001",
                InitialInput = "input",
                Status = WorkflowExecutionStatus.Paused,
                UpdatedTime = updatedTime,
                CompletedSteps = """["step-a"]""",
                StepOutputs = """{"step-a":"done"}""",
                StepsAwaitingApproval = """["step-b"]"""
            });

        var store = new DatabaseWorkflowCheckpointStore(repository.Object, Mock.Of<ILogger<DatabaseWorkflowCheckpointStore>>());

        var checkpoint = await store.GetCheckpointAsync("exec-001");

        checkpoint.ShouldNotBeNull();
        checkpoint!.UpdatedAt.ShouldBe(updatedTime);
        checkpoint.CompletedStepIds.ShouldContain("step-a");
        checkpoint.StepsAwaitingApproval.ShouldContain("step-b");
    }

    [Fact]
    public async Task GetCheckpointAsync_DeserializesStructuredStepOutputs()
    {
        var repository = new Mock<IRepository<WorkflowExecution, Guid>>();
        repository.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<WorkflowExecution, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowExecution
            {
                ExecutionId = "exec-structured-001",
                InitialInput = "input",
                Status = WorkflowExecutionStatus.Paused,
                UpdatedTime = DateTime.UtcNow,
                CompletedSteps = """["review"]""",
                StepOutputs = """{"review":{"text":"needs revision","metadata":{"verdict":"rework","feedback":"needs revision"}}}""",
                StepsAwaitingApproval = "[]"
            });

        var store = new DatabaseWorkflowCheckpointStore(repository.Object, Mock.Of<ILogger<DatabaseWorkflowCheckpointStore>>());

        var checkpoint = await store.GetCheckpointAsync("exec-structured-001");

        checkpoint.ShouldNotBeNull();
        checkpoint!.StepOutputs["review"].Text.ShouldBe("needs revision");
        checkpoint.StepOutputs["review"].Metadata.ShouldNotBeNull();
        checkpoint.StepOutputs["review"].Metadata!["verdict"].ShouldBe("rework");
    }

    private static WorkflowService CreateService(
        Mock<IRepository<WorkflowDefinition, Guid>> workflowRepository,
        Mock<IRepository<WorkflowExecution, Guid>> executionRepository,
        Mock<IWorkflowCheckpointStore> checkpointStore,
        Mock<IRunStore>? runStore = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(executionRepository.Object);
        if (runStore != null)
        {
            services.AddSingleton(runStore.Object);
        }
        services.AddScoped<IWorkflowNode, FixedResultNode>();
        services.AddScoped<IWorkflowNode, FailingResultNode>();
        services.AddScoped<IWorkflowNode, ApprovalNode>();
        services.AddScoped<WorkflowNodeExecutor>();
        services.AddScoped<WorkflowEngine>();
        var runRepository = new Mock<IRepository<AgentRun, Guid>>();
        runRepository.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<AgentRun, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRun?)null);
        var usageLogService = new Mock<IUsageLogService>();
        usageLogService.Setup(x => x.LogUsageAsync(
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
        var serviceProvider = services.BuildServiceProvider();

        var versionRepository = new Mock<IRepository<WorkflowDefinitionVersion, Guid>>();

        return new WorkflowService(
            workflowRepository.Object,
            executionRepository.Object,
            versionRepository.Object,
            runRepository.Object,
            Mock.Of<IRepository<Agent, Guid>>(),
            usageLogService.Object,
            Mock.Of<IQuotaService>(),
            checkpointStore.Object,
            serviceProvider.GetRequiredService<WorkflowEngine>(),
            serviceProvider);
    }

    private static string SerializeSteps(params WorkflowStepDto[] steps)
    {
        return JsonSerializer.Serialize(steps, TnziJsonDefaults.Options);
    }

    private sealed class FixedResultNode : IWorkflowNode
    {
        public string NodeType => "fixed-result";

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            var result = context.Step.Configuration?.GetValueOrDefault("result") ?? string.Empty;
            return Task.FromResult(new WorkflowNodeResult
            {
                Output = result
            });
        }
    }

    private sealed class FailingResultNode : IWorkflowNode
    {
        public string NodeType => "failing-result";

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
}
