using Tnzi.AI.Workflow.Options;

namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// WorkflowEngine 节点级错误策略（OnError: Fail / Skip / Continue）测试
/// </summary>
public class WorkflowNodeErrorPolicyTests
{
    // -----------------------------------------------------------------------
    // OnError = Fail (default)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task OnError_Fail_StopsWorkflow_WhenNodeFails()
    {
        var services = BuildServiceProvider(typeof(FailingNode), typeof(DownstreamNode));
        var engine = new WorkflowEngine(Mock.Of<ILogger<WorkflowEngine>>());

        var graph = new WorkflowGraph(
        [
            new WorkflowStepDto
            {
                StepId = "fail",
                OnError = NodeErrorPolicy.Fail,
                Configuration = new Dictionary<string, string> { ["nodeType"] = "fail-err-test" }
            },
            new WorkflowStepDto
            {
                StepId = "downstream",
                DependsOn = ["fail"],
                Configuration = new Dictionary<string, string> { ["nodeType"] = "downstream-err-test" }
            }
        ]);

        var result = await engine.ExecuteAsync(graph, "input", services);

        result.HasFailure.ShouldBeTrue();
        result.StepResults.ShouldContain(r => r.StepId == "fail");
        result.StepResults.ShouldNotContain(r => r.StepId == "downstream");
    }

    // -----------------------------------------------------------------------
    // OnError = Skip
    // -----------------------------------------------------------------------

    [Fact]
    public async Task OnError_Skip_ContinuesWorkflow_WithEmptyOutput()
    {
        var services = BuildServiceProvider(typeof(FailingNode), typeof(DownstreamNode));
        var engine = new WorkflowEngine(Mock.Of<ILogger<WorkflowEngine>>());

        var graph = new WorkflowGraph(
        [
            new WorkflowStepDto
            {
                StepId = "fail",
                OnError = NodeErrorPolicy.Skip,
                Configuration = new Dictionary<string, string> { ["nodeType"] = "fail-err-test" }
            },
            new WorkflowStepDto
            {
                StepId = "downstream",
                DependsOn = ["fail"],
                Configuration = new Dictionary<string, string> { ["nodeType"] = "downstream-err-test" }
            }
        ]);

        var result = await engine.ExecuteAsync(graph, "input", services);

        // Workflow must NOT fail
        result.HasFailure.ShouldBeFalse();
        // Both steps must appear in results
        result.StepResults.ShouldContain(r => r.StepId == "fail");
        result.StepResults.ShouldContain(r => r.StepId == "downstream");
        // Skipped node output should be empty
        result.StepResults.First(r => r.StepId == "fail").Output.ShouldBe(string.Empty);
        // Downstream ran successfully
        result.StepResults.First(r => r.StepId == "downstream").Output.ShouldBe("downstream-done");
    }

    // -----------------------------------------------------------------------
    // OnError = Continue
    // -----------------------------------------------------------------------

    [Fact]
    public async Task OnError_Continue_ContinuesWorkflow_WithErrorAsOutput()
    {
        var services = BuildServiceProvider(typeof(FailingNode), typeof(DownstreamNode));
        var engine = new WorkflowEngine(Mock.Of<ILogger<WorkflowEngine>>());

        var graph = new WorkflowGraph(
        [
            new WorkflowStepDto
            {
                StepId = "fail",
                OnError = NodeErrorPolicy.Continue,
                Configuration = new Dictionary<string, string> { ["nodeType"] = "fail-err-test" }
            },
            new WorkflowStepDto
            {
                StepId = "downstream",
                DependsOn = ["fail"],
                Configuration = new Dictionary<string, string> { ["nodeType"] = "downstream-err-test" }
            }
        ]);

        var result = await engine.ExecuteAsync(graph, "input", services);

        result.HasFailure.ShouldBeFalse();
        result.StepResults.ShouldContain(r => r.StepId == "fail");
        result.StepResults.ShouldContain(r => r.StepId == "downstream");
        // fail node output should contain the error text ("[Error: boom]")
        result.StepResults.First(r => r.StepId == "fail").Output.ShouldNotBeNullOrEmpty();
    }

    // -----------------------------------------------------------------------
    // Watchdog tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Watchdog_MarksRunningTimedOutExecution_AsFailed()
    {
        var staleExecution = new WorkflowExecution
        {
            Id = Guid.NewGuid(),
            ExecutionId = "stale-running-001",
            Status = WorkflowExecutionStatus.Running,
            UpdatedTime = DateTime.UtcNow.AddHours(-2) // older than 30-min default
        };

        var freshExecution = new WorkflowExecution
        {
            Id = Guid.NewGuid(),
            ExecutionId = "fresh-running-001",
            Status = WorkflowExecutionStatus.Running,
            UpdatedTime = DateTime.UtcNow.AddMinutes(-5) // within threshold
        };

        var updatedEntities = new List<WorkflowExecution>();
        var repo = BuildMockRepo([staleExecution, freshExecution], updatedEntities);
        var watchdog = new WorkflowWatchdogService(repo, Mock.Of<ILogger<WorkflowWatchdogService>>(), CreateOptions());

        var count = await watchdog.ScanAsync();

        count.ShouldBe(1);
        updatedEntities.ShouldHaveSingleItem();
        updatedEntities[0].ExecutionId.ShouldBe("stale-running-001");
        updatedEntities[0].Status.ShouldBe(WorkflowExecutionStatus.Failed);
        updatedEntities[0].CurrentWaitReason.ShouldBe("timed_out");
    }

    [Fact]
    public async Task Watchdog_MarksAwaitingApprovalTimedOut_AsFailed()
    {
        var staleApproval = new WorkflowExecution
        {
            Id = Guid.NewGuid(),
            ExecutionId = "stale-approval-001",
            Status = WorkflowExecutionStatus.AwaitingApproval,
            UpdatedTime = DateTime.UtcNow.AddDays(-8) // older than 7-day default
        };

        var updatedEntities = new List<WorkflowExecution>();
        var repo = BuildMockRepo([staleApproval], updatedEntities);
        var watchdog = new WorkflowWatchdogService(repo, Mock.Of<ILogger<WorkflowWatchdogService>>(), CreateOptions());

        var count = await watchdog.ScanAsync();

        count.ShouldBe(1);
        updatedEntities[0].Status.ShouldBe(WorkflowExecutionStatus.Failed);
    }

    [Fact]
    public async Task Watchdog_ReturnsZero_WhenDisabled()
    {
        var staleExecution = new WorkflowExecution
        {
            Id = Guid.NewGuid(),
            ExecutionId = "stale-001",
            Status = WorkflowExecutionStatus.Running,
            UpdatedTime = DateTime.UtcNow.AddHours(-2)
        };

        var updatedEntities = new List<WorkflowExecution>();
        var repo = BuildMockRepo([staleExecution], updatedEntities);
        var options = new StaticOptionsMonitor<WorkflowWatchdogOptions>(new WorkflowWatchdogOptions { Enabled = false });
        var watchdog = new WorkflowWatchdogService(repo, Mock.Of<ILogger<WorkflowWatchdogService>>(), options);

        var count = await watchdog.ScanAsync();

        count.ShouldBe(0);
        updatedEntities.ShouldBeEmpty();
    }

    [Fact]
    public async Task Watchdog_ReturnsZero_WhenNoTimedOutExecutions()
    {
        var freshExecution = new WorkflowExecution
        {
            Id = Guid.NewGuid(),
            ExecutionId = "fresh-001",
            Status = WorkflowExecutionStatus.Running,
            UpdatedTime = DateTime.UtcNow.AddMinutes(-1)
        };

        var updatedEntities = new List<WorkflowExecution>();
        var repo = BuildMockRepo([freshExecution], updatedEntities);
        var watchdog = new WorkflowWatchdogService(repo, Mock.Of<ILogger<WorkflowWatchdogService>>(), CreateOptions());

        var count = await watchdog.ScanAsync();

        count.ShouldBe(0);
        updatedEntities.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IServiceProvider BuildServiceProvider(params Type[] nodeTypes)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        foreach (var nodeType in nodeTypes)
        {
            services.AddScoped(typeof(IWorkflowNode), nodeType);
        }
        services.AddScoped<WorkflowNodeExecutor>();
        return services.BuildServiceProvider();
    }

    private static IRepository<WorkflowExecution, Guid> BuildMockRepo(
        IEnumerable<WorkflowExecution> data,
        List<WorkflowExecution> updatedEntities)
    {
        var dataList = data.ToList();

        var repo = new Mock<IRepository<WorkflowExecution, Guid>>();
        repo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<WorkflowExecution, bool>>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<WorkflowExecution, bool>>? pred, CancellationToken _) =>
            {
                if (pred == null) return dataList;
                var compiled = pred.Compile();
                return dataList.Where(compiled).ToList();
            });
        repo.Setup(r => r.UpdateAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowExecution, CancellationToken>((e, _) => updatedEntities.Add(e))
            .Returns(Task.CompletedTask);

        return repo.Object;
    }

    private static StaticOptionsMonitor<WorkflowWatchdogOptions> CreateOptions() =>
        new(new WorkflowWatchdogOptions
        {
            Enabled = true,
            RunningTimeout = TimeSpan.FromMinutes(30),
            WaitingTimeout = TimeSpan.FromDays(7)
        });

    // -----------------------------------------------------------------------
    // Test node implementations
    // -----------------------------------------------------------------------

    private sealed class FailingNode : IWorkflowNode
    {
        public string NodeType => "fail-err-test";

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
        public string NodeType => "downstream-err-test";

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkflowNodeResult { Output = "downstream-done" });
        }
    }
}
