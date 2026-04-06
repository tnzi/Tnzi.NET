
using System.Runtime.CompilerServices;

namespace Tnzi.AI.Tests;

public class AgentRuntimeWorkflowDispatchTests
{
    [Fact]
    public async Task RunAsync_WithWorkflowId_DelegatesToWorkflowService()
    {
        var workflowId = Guid.NewGuid();
        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.RunAsync(workflowId, "workflow input", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowExecutionResultDto>.Success(new WorkflowExecutionResultDto
            {
                ExecutionId = "wf-001",
                RunId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Output = "workflow result",
                Status = "Completed"
            }));

        var runtime = CreateRuntime(workflowService.Object);

        var result = await runtime.RunAsync(new AgentRunRequest
        {
            WorkflowId = workflowId,
            UserMessage = "workflow input"
        });

        result.Response.ShouldBe("workflow result");
        result.RunId.ShouldBe(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        result.Status.ShouldBe(AgentRunStatus.Completed);
        result.FinishReason.ShouldBe("completed");

        workflowService.Verify(x => x.RunAsync(workflowId, "workflow input", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithWorkflowId_PublishesWorkflowCompletedEvent()
    {
        var workflowId = Guid.NewGuid();
        var publishedEvents = new List<AgentRunCompletedEvent>();
        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.RunAsync(workflowId, "workflow input", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowExecutionResultDto>.Success(new WorkflowExecutionResultDto
            {
                ExecutionId = "wf-001",
                RunId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Output = "workflow result",
                Status = "Completed"
            }));

        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(x => x.PublishAsync(It.IsAny<AgentRunCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRunCompletedEvent, CancellationToken>((evt, _) => publishedEvents.Add(evt))
            .Returns(Task.CompletedTask);

        var runtime = CreateRuntime(workflowService.Object, eventBus: eventBus.Object);

        await runtime.RunAsync(new AgentRunRequest
        {
            WorkflowId = workflowId,
            UserMessage = "workflow input"
        });

        publishedEvents.Count.ShouldBe(1);
        publishedEvents[0].RunId.ShouldBe(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        publishedEvents[0].Provider.ShouldBe("Workflow");
        publishedEvents[0].Status.ShouldBe(AgentRunStatus.Completed.ToString());
        publishedEvents[0].FinishReason.ShouldBe(FinishReasons.Completed);
    }

    [Fact]
    public async Task RunStreamingAsync_WithWorkflowId_DelegatesToWorkflowService()
    {
        var workflowId = Guid.NewGuid();
        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.RunStreamingAsync(workflowId, "workflow input", null, It.IsAny<CancellationToken>()))
            .Returns(StreamWorkflowResults());

        var runtime = CreateRuntime(workflowService.Object);
        var chunks = new List<AgentStreamChunk>();

        await foreach (var chunk in runtime.RunStreamingAsync(new AgentRunRequest
        {
            WorkflowId = workflowId,
            UserMessage = "workflow input"
        }))
        {
            chunks.Add(chunk);
        }

        chunks.Count.ShouldBe(2);
        chunks[0].Text.ShouldBe("step output");
        chunks[1].Text.ShouldBe("done");
        chunks[1].FinishReason.ShouldBe("stop");

        workflowService.Verify(x => x.RunStreamingAsync(workflowId, "workflow input", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunStreamingAsync_WithWorkflowId_PublishesWorkflowCompletedEvent()
    {
        var workflowId = Guid.NewGuid();
        var publishedEvents = new List<AgentRunCompletedEvent>();
        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.RunStreamingAsync(workflowId, "workflow input", null, It.IsAny<CancellationToken>()))
            .Returns(StreamWorkflowResults());

        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(x => x.PublishAsync(It.IsAny<AgentRunCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRunCompletedEvent, CancellationToken>((evt, _) => publishedEvents.Add(evt))
            .Returns(Task.CompletedTask);

        var runtime = CreateRuntime(workflowService.Object, eventBus: eventBus.Object);

        await foreach (var _ in runtime.RunStreamingAsync(new AgentRunRequest
        {
            WorkflowId = workflowId,
            UserMessage = "workflow input"
        }))
        {
        }

        publishedEvents.Count.ShouldBe(1);
        publishedEvents[0].Provider.ShouldBe("Workflow");
        publishedEvents[0].Status.ShouldBe(AgentRunStatus.Completed.ToString());
        publishedEvents[0].FinishReason.ShouldBe(FinishReasons.Stop);
    }

    [Fact]
    public async Task RunStreamingAsync_WithWorkflowId_WhenWorkflowPartialFailure_MapsFailedFinishReason()
    {
        var workflowId = Guid.NewGuid();
        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.RunStreamingAsync(workflowId, "workflow input", null, It.IsAny<CancellationToken>()))
            .Returns(StreamWorkflowPartialFailureResults());

        var runtime = CreateRuntime(workflowService.Object);
        var chunks = new List<AgentStreamChunk>();

        await foreach (var chunk in runtime.RunStreamingAsync(new AgentRunRequest
        {
            WorkflowId = workflowId,
            UserMessage = "workflow input"
        }))
        {
            chunks.Add(chunk);
        }

        chunks.Count.ShouldBe(1);
        chunks[0].Text.ShouldBe("partially failed");
        chunks[0].FinishReason.ShouldBe(FinishReasons.Failed);
    }

    [Fact]
    public async Task RunAsync_WithWorkflowId_WhenWorkflowFails_MapsFailedStatus()
    {
        var workflowId = Guid.NewGuid();
        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.RunAsync(workflowId, "workflow input", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowExecutionResultDto>.Success(new WorkflowExecutionResultDto
            {
                ExecutionId = "wf-failed-001",
                Output = "workflow failed",
                Status = "Failed"
            }));

        var runtime = CreateRuntime(workflowService.Object);

        var result = await runtime.RunAsync(new AgentRunRequest
        {
            WorkflowId = workflowId,
            UserMessage = "workflow input"
        });

        result.Response.ShouldBe("workflow failed");
        result.Status.ShouldBe(AgentRunStatus.Failed);
        result.FinishReason.ShouldBe("failed");
    }

    [Fact]
    public async Task RunAsync_WithWorkflowId_WhenWorkflowPartialFailure_MapsFailedStatus()
    {
        var workflowId = Guid.NewGuid();
        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.RunAsync(workflowId, "workflow input", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowExecutionResultDto>.Success(new WorkflowExecutionResultDto
            {
                ExecutionId = "wf-partial-001",
                Output = "partial workflow failure",
                Status = "PartialFailure(step-a)"
            }));

        var runtime = CreateRuntime(workflowService.Object);

        var result = await runtime.RunAsync(new AgentRunRequest
        {
            WorkflowId = workflowId,
            UserMessage = "workflow input"
        });

        result.Response.ShouldBe("partial workflow failure");
        result.Status.ShouldBe(AgentRunStatus.Failed);
        result.FinishReason.ShouldBe(FinishReasons.Failed);
    }

    [Fact]
    public async Task RunAsync_WithWorkflowId_WhenWorkflowAwaitingInput_MapsRequiresClarificationStatus()
    {
        var workflowId = Guid.NewGuid();
        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.RunAsync(workflowId, "workflow input", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowExecutionResultDto>.Success(new WorkflowExecutionResultDto
            {
                ExecutionId = "wf-awaiting-input-001",
                Output = "Need more input",
                Status = "AwaitingInput"
            }));

        var runtime = CreateRuntime(workflowService.Object);

        var result = await runtime.RunAsync(new AgentRunRequest
        {
            WorkflowId = workflowId,
            UserMessage = "workflow input"
        });

        result.Response.ShouldBe("Need more input");
        result.Status.ShouldBe(AgentRunStatus.RequiresClarification);
        result.FinishReason.ShouldBe(FinishReasons.RequiresClarification);
    }

    [Fact]
    public async Task RunAsync_WhenAgentResolutionFails_ThrowsBusinessException()
    {
        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentResolution.Failure("test", null, null, ErrorCodes.AgentNotFound));

        var runtime = CreateRuntime(Mock.Of<IWorkflowService>(), resolver.Object);

        var ex = await Should.ThrowAsync<Tnzi.Exceptions.BusinessException>(() => runtime.RunAsync(new AgentRunRequest
        {
            UserMessage = "hello"
        }));

        ex.Code.ShouldBe(ErrorCodes.AgentNotFound);
        ex.HttpStatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task RunAsync_RootExecution_ClearsScopedPropertiesBetweenRuns()
    {
        var workflowId = Guid.NewGuid();
        var accessor = new AgentExecutionContextAccessor();
        var workflowService = new Mock<IWorkflowService>();
        var observedStates = new List<bool>();

        workflowService
            .Setup(x => x.RunAsync(workflowId, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                observedStates.Add(accessor.Properties.ContainsKey(ContextPropertyKeys.Todos));
                accessor.Properties[ContextPropertyKeys.Todos] = new List<TodoItemDto>
                {
                    new() { Content = "Inner state", Status = TodoStatus.Pending, Order = 1 }
                };
            })
            .ReturnsAsync(Result<WorkflowExecutionResultDto>.Success(new WorkflowExecutionResultDto
            {
                ExecutionId = "wf-cleanup",
                Output = "ok",
                Status = "Completed"
            }));

        accessor.Properties[ContextPropertyKeys.Todos] = new List<TodoItemDto>
        {
            new() { Content = "Stale state", Status = TodoStatus.Pending, Order = 1 }
        };

        var runtime = CreateRuntime(workflowService.Object, accessor: accessor);

        await runtime.RunAsync(new AgentRunRequest { WorkflowId = workflowId, UserMessage = "one" });
        await runtime.RunAsync(new AgentRunRequest { WorkflowId = workflowId, UserMessage = "two" });

        observedStates.ShouldBe([false, false]);
    }

    [Fact]
    public async Task RunStreamingAsync_ExternalCli_AnnotatesChunksWithResolvedModel()
    {
        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentResolution.SuccessWithoutExecutor(
                "test-provider",
                "gpt-5-think",
                null,
                null,
                AgentExecutionMode.ExternalCli));

        var cliExecutor = new Mock<IExternalCliExecutor>();
        cliExecutor.Setup(x => x.ExecuteCliStreamingAsync(It.IsAny<AiMiddlewareContext>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(
            [
                new AgentStreamChunk { Text = "hello" },
                new AgentStreamChunk { FinishReason = FinishReasons.Stop }
            ]));

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IExternalCliExecutor>(cliExecutor.Object)
            .BuildServiceProvider();

        var runtime = CreateRuntime(Mock.Of<IWorkflowService>(), resolver.Object, serviceProvider: serviceProvider);
        var chunks = new List<AgentStreamChunk>();

        await foreach (var chunk in runtime.RunStreamingAsync(new AgentRunRequest
        {
            UserMessage = "hello"
        }))
        {
            chunks.Add(chunk);
        }

        chunks.Count.ShouldBe(2);
        chunks[0].Model.ShouldBe("gpt-5-think");
        chunks[1].Model.ShouldBe("gpt-5-think");
    }

    [Fact]
    public async Task RunAsync_EnableRunTracking_WhenFinishReasonIsFailure_MarksRunFailed()
    {
        var updatedRuns = new List<AgentRun>();
        var runStore = CreateRunStore(updatedRuns);
        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentResolution.SuccessWithoutExecutor("test-provider", "gpt-5", null, null, AgentExecutionMode.ExternalCli));

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IAiMiddleware>(new StaticResultMiddleware(new AgentRunResult
            {
                Response = "blocked by policy",
                FinishReason = FinishReasons.GuardrailRejected
            }))
            .BuildServiceProvider();

        var runtime = new AgentRuntime(
            resolver.Object,
            Mock.Of<IAgentFactory>(),
            Mock.Of<IRepository<Agent, Guid>>(),
            runStore.Object,
            CreateTraceStore().Object,
            Mock.Of<IWorkflowService>(),
            new AgentExecutionContextAccessor(),
            serviceProvider,
            Mock.Of<IServiceScopeFactory>(),
            Mock.Of<IOptionsMonitor<AIOptions>>(),
            Mock.Of<ILogger<AgentRuntime>>());

        var result = await runtime.RunAsync(new AgentRunRequest
        {
            UserMessage = "hello",
            EnableRunTracking = true
        });

        result.Status.ShouldBe(AgentRunStatus.Failed);
        updatedRuns.Last().Status.ShouldBe(AgentRunStatus.Failed);
        updatedRuns.Last().Error.ShouldBe("blocked by policy");
    }

    [Fact]
    public async Task RunStreamingAsync_EnableRunTracking_WhenFinishReasonRequiresClarification_MarksRunRequiresClarification()
    {
        var updatedRuns = new List<AgentRun>();
        var runStore = CreateRunStore(updatedRuns);
        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentResolution.SuccessWithoutExecutor("test-provider", "gpt-5", null, null, AgentExecutionMode.ExternalCli));

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IAiMiddleware>(new StaticStreamingMiddleware(
            [
                new AgentStreamChunk
                {
                    Text = "need more info",
                    FinishReason = FinishReasons.RequiresClarification
                }
            ]))
            .BuildServiceProvider();

        var runtime = new AgentRuntime(
            resolver.Object,
            Mock.Of<IAgentFactory>(),
            Mock.Of<IRepository<Agent, Guid>>(),
            runStore.Object,
            CreateTraceStore().Object,
            Mock.Of<IWorkflowService>(),
            new AgentExecutionContextAccessor(),
            serviceProvider,
            Mock.Of<IServiceScopeFactory>(),
            Mock.Of<IOptionsMonitor<AIOptions>>(),
            Mock.Of<ILogger<AgentRuntime>>());

        await foreach (var _ in runtime.RunStreamingAsync(new AgentRunRequest
        {
            UserMessage = "hello",
            EnableRunTracking = true
        }))
        {
        }

        updatedRuns.Last().Status.ShouldBe(AgentRunStatus.RequiresClarification);
        updatedRuns.Last().Error.ShouldBeNull();
    }

    [Fact]
    public async Task RunAsync_WithoutRunTracking_WhenFailureOccurs_PublishesFailedEventWithResolvedProvider()
    {
        var publishedEvents = new List<AgentRunCompletedEvent>();
        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentResolution.SuccessWithoutExecutor("resolved-provider", "gpt-5-think", null, null, AgentExecutionMode.ExternalCli));

        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(x => x.PublishAsync(It.IsAny<AgentRunCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRunCompletedEvent, CancellationToken>((evt, _) => publishedEvents.Add(evt))
            .Returns(Task.CompletedTask);

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IAiMiddleware>(new StaticResultMiddleware(new AgentRunResult
            {
                Response = "guardrail blocked",
                FinishReason = FinishReasons.GuardrailRejected,
                Model = "gpt-5-think"
            }))
            .BuildServiceProvider();

        var runtime = new AgentRuntime(
            resolver.Object,
            Mock.Of<IAgentFactory>(),
            Mock.Of<IRepository<Agent, Guid>>(),
            Mock.Of<IRunStore>(),
            CreateTraceStore().Object,
            Mock.Of<IWorkflowService>(),
            new AgentExecutionContextAccessor(),
            serviceProvider,
            Mock.Of<IServiceScopeFactory>(),
            Mock.Of<IOptionsMonitor<AIOptions>>(),
            Mock.Of<ILogger<AgentRuntime>>(),
            eventBus.Object);

        var result = await runtime.RunAsync(new AgentRunRequest
        {
            UserMessage = "hello"
        });

        result.FinishReason.ShouldBe(FinishReasons.GuardrailRejected);
        publishedEvents.Count.ShouldBe(1);
        publishedEvents[0].Provider.ShouldBe("resolved-provider");
        publishedEvents[0].Model.ShouldBe("gpt-5-think");
        publishedEvents[0].Status.ShouldBe(AgentRunStatus.Failed.ToString());
        publishedEvents[0].FinishReason.ShouldBe(FinishReasons.GuardrailRejected);
    }

    [Fact]
    public async Task RunStreamingAsync_WithWorkflowId_WhenWorkflowAwaitingInput_EmitsRequiresClarificationAndPublishesTerminalEvent()
    {
        var workflowId = Guid.NewGuid();
        var publishedEvents = new List<AgentRunCompletedEvent>();
        var workflowService = new Mock<IWorkflowService>();
        workflowService.Setup(x => x.RunStreamingAsync(workflowId, "workflow input", null, It.IsAny<CancellationToken>()))
            .Returns(StreamWorkflowAwaitingInputResults());

        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(x => x.PublishAsync(It.IsAny<AgentRunCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRunCompletedEvent, CancellationToken>((evt, _) => publishedEvents.Add(evt))
            .Returns(Task.CompletedTask);

        var runtime = CreateRuntime(workflowService.Object, eventBus: eventBus.Object);
        var chunks = new List<AgentStreamChunk>();

        await foreach (var chunk in runtime.RunStreamingAsync(new AgentRunRequest
        {
            WorkflowId = workflowId,
            UserMessage = "workflow input"
        }))
        {
            chunks.Add(chunk);
        }

        chunks.Count.ShouldBe(2);
        chunks[1].FinishReason.ShouldBe(FinishReasons.RequiresClarification);
        publishedEvents.Count.ShouldBe(1);
        publishedEvents[0].Status.ShouldBe(AgentRunStatus.RequiresClarification.ToString());
        publishedEvents[0].FinishReason.ShouldBe(FinishReasons.RequiresClarification);
    }

    [Fact]
    public async Task RunAsync_NewThreadFailure_DoesNotTriggerTitleGeneration()
    {
        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentResolution.SuccessWithoutExecutor("resolved-provider", "gpt-5", null, null, AgentExecutionMode.ExternalCli));

        var scopeFactory = new Mock<IServiceScopeFactory>();
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IAiMiddleware>(new ThreadSetupResultMiddleware(
                new AgentRunResult
                {
                    Response = "workflow execution failed",
                    FinishReason = FinishReasons.Failed
                },
                Guid.Parse("33333333-3333-3333-3333-333333333333")))
            .BuildServiceProvider();

        var runtime = CreateRuntime(
            Mock.Of<IWorkflowService>(),
            resolver.Object,
            serviceProvider: serviceProvider,
            scopeFactory: scopeFactory.Object);

        await runtime.RunAsync(new AgentRunRequest
        {
            UserMessage = "hello"
        });

        scopeFactory.Verify(x => x.CreateScope(), Times.Never);
    }

    private static AgentRuntime CreateRuntime(
        IWorkflowService workflowService,
        IAgentResolver? resolver = null,
        IAgentExecutionContextAccessor? accessor = null,
        IServiceProvider? serviceProvider = null,
        IServiceScopeFactory? scopeFactory = null,
        IEventBus? eventBus = null)
    {
        var sp = serviceProvider ?? new ServiceCollection().BuildServiceProvider();
        return new AgentRuntime(
            resolver ?? Mock.Of<IAgentResolver>(),
            Mock.Of<IAgentFactory>(),
            Mock.Of<IRepository<Agent, Guid>>(),
            Mock.Of<IRunStore>(),
            Mock.Of<ITraceStore>(),
            workflowService,
            accessor ?? new AgentExecutionContextAccessor(),
            sp,
            scopeFactory ?? Mock.Of<IServiceScopeFactory>(),
            Mock.Of<IOptionsMonitor<AIOptions>>(),
            Mock.Of<ILogger<AgentRuntime>>(),
            eventBus);
    }

    private static Mock<IRunStore> CreateRunStore(List<AgentRun> updatedRuns)
    {
        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRun run, CancellationToken _) =>
            {
                run.Id = Guid.NewGuid();
                return run;
            });
        runStore.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRun, CancellationToken>((run, _) => updatedRuns.Add(CloneRun(run)))
            .Returns(Task.CompletedTask);
        return runStore;
    }

    private static Mock<ITraceStore> CreateTraceStore()
    {
        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(x => x.AddAsync(It.IsAny<AgentRunTrace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRunTrace trace, CancellationToken _) => trace);
        return traceStore;
    }

    private static AgentRun CloneRun(AgentRun run)
    {
        return new AgentRun
        {
            Id = run.Id,
            AgentId = run.AgentId,
            ThreadId = run.ThreadId,
            WorkflowDefinitionId = run.WorkflowDefinitionId,
            Status = run.Status,
            ExecutionMode = run.ExecutionMode,
            InputSummary = run.InputSummary,
            OutputSummary = run.OutputSummary,
            Error = run.Error,
            DurationMs = run.DurationMs,
            TotalInputTokens = run.TotalInputTokens,
            TotalOutputTokens = run.TotalOutputTokens
        };
    }

    private static async IAsyncEnumerable<AgentStreamChunk> ToAsyncEnumerable(IEnumerable<AgentStreamChunk> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.CompletedTask;
        }
    }

    private static async IAsyncEnumerable<WorkflowExecutionResultDto> StreamWorkflowResults()
    {
        yield return new WorkflowExecutionResultDto
        {
            ExecutionId = "wf-001",
            Output = "step output",
            Status = "Step 'step-a'"
        };

        await Task.Yield();

        yield return new WorkflowExecutionResultDto
        {
            ExecutionId = "wf-001",
            Output = "done",
            Status = "Completed"
        };
    }

    private static async IAsyncEnumerable<WorkflowExecutionResultDto> StreamWorkflowPartialFailureResults()
    {
        await Task.Yield();
        yield return new WorkflowExecutionResultDto
        {
            ExecutionId = "wf-partial-001",
            Output = "partially failed",
            Status = "PartialFailure(step-a)"
        };
    }

    private static async IAsyncEnumerable<WorkflowExecutionResultDto> StreamWorkflowAwaitingInputResults()
    {
        yield return new WorkflowExecutionResultDto
        {
            ExecutionId = "wf-awaiting-input-001",
            Output = "step output",
            Status = "Step 'step-a'"
        };

        await Task.Yield();

        yield return new WorkflowExecutionResultDto
        {
            ExecutionId = "wf-awaiting-input-001",
            Output = "Need more input",
            Status = "AwaitingInput"
        };
    }

    private sealed class StaticResultMiddleware : IAiMiddleware
    {
        private readonly AgentRunResult _result;

        public StaticResultMiddleware(AgentRunResult result)
        {
            _result = result;
        }

        public int Order => 0;

        public Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);

        public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }
    }

    private sealed class StaticStreamingMiddleware : IAiMiddleware
    {
        private readonly IReadOnlyList<AgentStreamChunk> _chunks;

        public StaticStreamingMiddleware(IReadOnlyList<AgentStreamChunk> chunks)
        {
            _chunks = chunks;
        }

        public int Order => 0;

        public Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
            => next(context, cancellationToken);

        public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var chunk in _chunks)
            {
                yield return chunk;
                await Task.Yield();
            }
        }
    }

    private sealed class ThreadSetupResultMiddleware : IAiMiddleware
    {
        private readonly AgentRunResult _result;
        private readonly Guid _threadId;

        public ThreadSetupResultMiddleware(AgentRunResult result, Guid threadId)
        {
            _result = result;
            _threadId = threadId;
        }

        public int Order => 0;

        public Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
        {
            context.IsNewThread = true;
            context.Request.ThreadId = _threadId;
            return Task.FromResult(_result);
        }

        public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }
    }
}
