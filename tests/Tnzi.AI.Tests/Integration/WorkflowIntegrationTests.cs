namespace Tnzi.AI.Tests.Integration;

/// <summary>
/// Workflow DAG 执行集成测试 - 测试 WorkflowEngine 跨节点交互、条件路由、检查点恢复、
/// 并行执行、环路检测和错误处理
/// </summary>
public class WorkflowIntegrationTests
{
    /// <summary>
    /// 构建 IServiceProvider，注册所有需要的 IWorkflowNode 实现 + Mock AI 依赖
    /// </summary>
    private static IServiceProvider BuildServiceProvider(Dictionary<string, string>? agentResponses = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Mock AgentFactory - 根据 name 参数返回预设响应
        var responses = agentResponses ?? new Dictionary<string, string>();
        var mockFactory = new Mock<IAgentFactory>();
        mockFactory
            .Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(),
                It.IsAny<int?>(), It.IsAny<AgentExecutorOptions?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? _, string? _, string? _, string? name,
                IEnumerable<string>? _, double? _, int? _,
                AgentExecutorOptions? _, IEnumerable<string>? _,
                IEnumerable<string>? _, Guid? _, CancellationToken _) =>
            {
                var responseText = responses.GetValueOrDefault(name ?? "", "default response");

                var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText))
                {
                    Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 20 }
                };

                var mockClient = new Mock<IChatClient>();
                mockClient
                    .Setup(c => c.GetResponseAsync(
                        It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(chatResponse);

                return new AgentExecutor(mockClient.Object, new AgentExecutorOptions { Name = name ?? "agent" });
            });

        services.AddSingleton(mockFactory.Object);

        // WorkflowNodeServiceContext
        var nodeCtxMock = new Mock<IWorkflowNodeServiceContext>();
        nodeCtxMock.Setup(c => c.AgentFactory).Returns(mockFactory.Object);
        nodeCtxMock.Setup(c => c.AgentRepository).Returns((IRepository<Agent, Guid>?)null);

        // 延迟绑定 CreateScope - 需要等 SP 构建完成后才能调用
        IServiceProvider? builtSp = null;
        nodeCtxMock.Setup(c => c.CreateScope()).Returns(() => builtSp!.CreateScope());

        services.AddSingleton(nodeCtxMock.Object);

        // 注册所有节点类型
        services.AddScoped<IWorkflowNode, AgentNode>();
        services.AddScoped<IWorkflowNode, TransformNode>();
        services.AddScoped<IWorkflowNode, ConditionalNode>();
        services.AddScoped<IWorkflowNode, ParallelNode>();
        services.AddScoped<IWorkflowNode, ApprovalNode>();

        // WorkflowNodeExecutor + WorkflowEngine
        services.AddScoped<WorkflowNodeExecutor>();
        services.AddScoped<WorkflowEngine>();

        var sp = services.BuildServiceProvider();
        builtSp = sp;
        return sp;
    }

    #region 1. ParallelNode - 两个并行分支均执行

    [Fact]
    public async Task ParallelBranches_BothExecute_AndMergeAtDownstream()
    {
        // Arrange: A → (B, C 并行) → D
        // B 和 C 没有相互依赖，只依赖 A
        // D 依赖 B 和 C
        var sp = BuildServiceProvider(new Dictionary<string, string>
        {
            ["step-a"] = "Step A result",
            ["step-b"] = "Branch B result",
            ["step-c"] = "Branch C result",
            ["step-d"] = "Final merged result"
        });

        var nodes = new List<WorkflowStepDto>
        {
            new() { StepId = "step-a", Configuration = new() { ["nodeType"] = "agent" } },
            new() { StepId = "step-b", DependsOn = ["step-a"], Configuration = new() { ["nodeType"] = "agent" } },
            new() { StepId = "step-c", DependsOn = ["step-a"], Configuration = new() { ["nodeType"] = "agent" } },
            new() { StepId = "step-d", DependsOn = ["step-b", "step-c"], Configuration = new() { ["nodeType"] = "agent" } }
        };

        var graph = new WorkflowGraph(nodes);
        var engine = sp.GetRequiredService<WorkflowEngine>();

        // Act
        var result = await engine.ExecuteAsync(graph, "initial input", sp);

        // Assert
        result.HasFailure.ShouldBeFalse();
        result.StepResults.Count.ShouldBe(4);

        // B 和 C 都执行了
        result.StepResults.ShouldContain(r => r.StepId == "step-b" && !r.Skipped);
        result.StepResults.ShouldContain(r => r.StepId == "step-c" && !r.Skipped);

        // D 是最后执行的
        result.FinalOutput.ShouldBe("Final merged result");

        // 所有步骤的输出都可以从 State 中获取
        result.State.GetOutputText("step-a").ShouldBe("Step A result");
        result.State.GetOutputText("step-b").ShouldBe("Branch B result");
        result.State.GetOutputText("step-c").ShouldBe("Branch C result");
        result.State.GetOutputText("step-d").ShouldBe("Final merged result");
    }

    #endregion

    #region 2. ConditionalNode - 条件分支路由

    [Fact]
    public async Task ConditionalBranching_RoutesToCorrectBranch()
    {
        // Arrange: Agent → (BranchA | BranchB) 通过条件边路由
        // Agent 输出包含 "approved" → 路由到 branch-a
        // 引擎逻辑：ConditionalNode 的 RouteTo 作为目标节点 ID 直接使用，
        // 因此此测试直接在 Agent 输出上使用 OutputContains 条件边
        var sp = BuildServiceProvider(new Dictionary<string, string>
        {
            ["agent-step"] = "The result is: approved",
            ["branch-a"] = "Branch A executed",
            ["branch-b"] = "Branch B executed"
        });

        var nodes = new List<WorkflowStepDto>
        {
            new()
            {
                StepId = "agent-step",
                Configuration = new() { ["nodeType"] = "agent" }
            },
            new()
            {
                StepId = "branch-a",
                DependsOn = ["agent-step"],
                Configuration = new() { ["nodeType"] = "agent" }
            },
            new()
            {
                StepId = "branch-b",
                DependsOn = ["agent-step"],
                Configuration = new() { ["nodeType"] = "agent" }
            }
        };

        // 条件边：从 agent-step 路由，输出包含 "approved" → branch-a, 默认 → branch-b
        var conditionalEdges = new List<ConditionalEdge>
        {
            new()
            {
                FromNodeId = "agent-step",
                Routes = new Dictionary<string, string> { ["approved"] = "branch-a" },
                DefaultTarget = "branch-b",
                ConditionType = EdgeConditionType.OutputContains
            }
        };

        var graph = new WorkflowGraph(nodes, conditionalEdges);
        var engine = sp.GetRequiredService<WorkflowEngine>();

        // Act
        var result = await engine.ExecuteAsync(graph, "check this", sp);

        // Assert
        result.HasFailure.ShouldBeFalse();

        // branch-a 应被执行（输出包含 "approved"）
        result.StepResults.ShouldContain(r => r.StepId == "branch-a" && !r.Skipped);

        // branch-b 应被跳过（条件路由排除了它）
        var branchB = result.StepResults.FirstOrDefault(r => r.StepId == "branch-b");
        if (branchB != null)
        {
            branchB.Skipped.ShouldBeTrue();
        }

        result.FinalOutput.ShouldBe("Branch A executed");
    }

    #endregion

    #region 3. Checkpoint/Resume - ApprovalNode 暂停 + 恢复

    [Fact]
    public async Task CheckpointResume_PausesAtApproval_ThenResumesAndCompletes()
    {
        // Arrange: Agent → Approval → Agent2
        var sp = BuildServiceProvider(new Dictionary<string, string>
        {
            ["agent-1"] = "Prepared content for review",
            ["agent-2"] = "Final result after approval"
        });

        var checkpointStore = new InMemoryCheckpointStore();

        var nodes = new List<WorkflowStepDto>
        {
            new()
            {
                StepId = "agent-1",
                Configuration = new() { ["nodeType"] = "agent" }
            },
            new()
            {
                StepId = "approval-step",
                DependsOn = ["agent-1"],
                Configuration = new() { ["nodeType"] = "approval" }
            },
            new()
            {
                StepId = "agent-2",
                DependsOn = ["approval-step"],
                Configuration = new() { ["nodeType"] = "agent" }
            }
        };

        var graph = new WorkflowGraph(nodes);
        var engine = sp.GetRequiredService<WorkflowEngine>();
        var executionId = Guid.NewGuid().ToString("N");

        // Act 1: 首次执行 - 应在 ApprovalNode 处暂停
        var options = new WorkflowExecutionOptions
        {
            ExecutionId = executionId,
            CheckpointStore = checkpointStore
        };

        var result1 = await engine.ExecuteAsync(graph, "initial input", sp, options);

        // Assert 1: 暂停在审批节点
        // ApprovalNode 通过 CheckInterruptAsync 返回 Approval 类型中断，
        // 引擎在 HandleApprovalInterruptAsync 中对 Approval 类型保持向后兼容，同时设置两个标志
        result1.AwaitingApproval.ShouldBeTrue();
        result1.AwaitingApprovalStepId.ShouldBe("approval-step");
        result1.AwaitingInterrupt.ShouldNotBeNull();
        result1.AwaitingInterrupt!.Type.ShouldBe(InterruptType.Approval);

        // 检查点已保存
        var checkpoint = await checkpointStore.GetCheckpointAsync(executionId);
        checkpoint.ShouldNotBeNull();
        checkpoint!.CompletedStepIds.ShouldContain("agent-1");

        // Act 2: 恢复执行 - 提交审批通过
        var resumeOptions = new WorkflowExecutionOptions
        {
            ExecutionId = executionId,
            CheckpointStore = checkpointStore,
            Resume = true,
            ResumeStepId = "approval-step",
            ResumeData = new Dictionary<string, object>
            {
                ["approved"] = true,
                ["comment"] = "Looks good"
            }
        };

        var result2 = await engine.ExecuteAsync(graph, "initial input", sp, resumeOptions);

        // Assert 2: 恢复后完成
        result2.HasFailure.ShouldBeFalse();
        result2.AwaitingInterrupt.ShouldBeNull();
        result2.StepResults.ShouldContain(r => r.StepId == "agent-2" && !r.Skipped);
        result2.FinalOutput.ShouldBe("Final result after approval");
    }

    #endregion

    #region 4. Multi-step DAG - Agent→Transform→Agent 数据流转

    [Fact]
    public async Task MultiStepDag_DataFlowsBetweenNodes()
    {
        // Arrange: Agent1 → Transform(uppercase) → Agent2
        // Agent1 输出 → Transform 转大写 → Agent2 接收大写输入
        var sp = BuildServiceProvider(new Dictionary<string, string>
        {
            ["agent-1"] = "hello world",
            ["agent-2"] = "Processed uppercase input"
        });

        var nodes = new List<WorkflowStepDto>
        {
            new()
            {
                StepId = "agent-1",
                Configuration = new() { ["nodeType"] = "agent" }
            },
            new()
            {
                StepId = "transform-step",
                DependsOn = ["agent-1"],
                Configuration = new() { ["nodeType"] = "transform", ["transformType"] = "uppercase" }
            },
            new()
            {
                StepId = "agent-2",
                DependsOn = ["transform-step"],
                Configuration = new() { ["nodeType"] = "agent" }
            }
        };

        var graph = new WorkflowGraph(nodes);
        var engine = sp.GetRequiredService<WorkflowEngine>();

        // Act
        var result = await engine.ExecuteAsync(graph, "initial input", sp);

        // Assert
        result.HasFailure.ShouldBeFalse();
        result.StepResults.Count.ShouldBe(3);

        // Transform 节点输出是大写的
        result.State.GetOutputText("transform-step").ShouldBe("HELLO WORLD");

        // Agent2 是最终步骤
        result.FinalOutput.ShouldBe("Processed uppercase input");

        // 数据链路：agent-1 → transform → agent-2
        var stepOrder = result.StepResults.Select(r => r.StepId).ToList();
        stepOrder.IndexOf("agent-1").ShouldBeLessThan(stepOrder.IndexOf("transform-step"));
        stepOrder.IndexOf("transform-step").ShouldBeLessThan(stepOrder.IndexOf("agent-2"));
    }

    #endregion

    #region 5. Cycle detection - 环路校验

    [Fact]
    public void CycleDetection_ThrowsOnCircularDependencies()
    {
        // Arrange: A → B → C → A（循环）
        var nodes = new List<WorkflowStepDto>
        {
            new() { StepId = "a", DependsOn = ["c"] },
            new() { StepId = "b", DependsOn = ["a"] },
            new() { StepId = "c", DependsOn = ["b"] }
        };

        // Act & Assert: WorkflowGraph 构造函数中的拓扑排序应检测到环路
        var ex = Should.Throw<InvalidOperationException>(() => new WorkflowGraph(nodes));
        ex.Message.ShouldContain("circular");
    }

    #endregion

    #region 6. Error in node - 节点失败传播

    [Fact]
    public async Task NodeFailure_SetsWorkflowStatusToFailed()
    {
        // Arrange: Agent1(成功) → Agent2(抛异常) → Agent3
        // Agent2 的 ChatClient 抛出异常
        var services = new ServiceCollection();
        services.AddLogging();

        // 自定义 factory：agent-1 正常，agent-2 抛异常
        var mockFactory = new Mock<IAgentFactory>();
        mockFactory
            .Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(),
                It.IsAny<int?>(), It.IsAny<AgentExecutorOptions?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? _, string? _, string? _, string? name,
                IEnumerable<string>? _, double? _, int? _,
                AgentExecutorOptions? _, IEnumerable<string>? _,
                IEnumerable<string>? _, Guid? _, CancellationToken _) =>
            {
                var mockClient = new Mock<IChatClient>();

                if (name == "agent-2")
                {
                    mockClient
                        .Setup(c => c.GetResponseAsync(
                            It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new InvalidOperationException("LLM provider error: model overloaded"));
                }
                else
                {
                    var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, $"Output from {name}"));
                    mockClient
                        .Setup(c => c.GetResponseAsync(
                            It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(response);
                }

                return new AgentExecutor(mockClient.Object, new AgentExecutorOptions { Name = name ?? "agent" });
            });

        services.AddSingleton(mockFactory.Object);

        var nodeCtxMock = new Mock<IWorkflowNodeServiceContext>();
        nodeCtxMock.Setup(c => c.AgentFactory).Returns(mockFactory.Object);
        nodeCtxMock.Setup(c => c.AgentRepository).Returns((IRepository<Agent, Guid>?)null);

        IServiceProvider? builtSp = null;
        nodeCtxMock.Setup(c => c.CreateScope()).Returns(() => builtSp!.CreateScope());

        services.AddSingleton(nodeCtxMock.Object);
        services.AddScoped<IWorkflowNode, AgentNode>();
        services.AddScoped<IWorkflowNode, TransformNode>();
        services.AddScoped<IWorkflowNode, ConditionalNode>();
        services.AddScoped<IWorkflowNode, ParallelNode>();
        services.AddScoped<IWorkflowNode, ApprovalNode>();
        services.AddScoped<WorkflowNodeExecutor>();
        services.AddScoped<WorkflowEngine>();

        var sp = services.BuildServiceProvider();
        builtSp = sp;

        var nodes = new List<WorkflowStepDto>
        {
            new() { StepId = "agent-1", Configuration = new() { ["nodeType"] = "agent" } },
            new() { StepId = "agent-2", DependsOn = ["agent-1"], Configuration = new() { ["nodeType"] = "agent" } },
            new() { StepId = "agent-3", DependsOn = ["agent-2"], Configuration = new() { ["nodeType"] = "agent" } }
        };

        var graph = new WorkflowGraph(nodes);
        var engine = sp.GetRequiredService<WorkflowEngine>();

        // Act
        var result = await engine.ExecuteAsync(graph, "test input", sp);

        // Assert
        result.HasFailure.ShouldBeTrue();
        result.StatusText.ShouldBe("Failed");

        // agent-1 成功执行
        result.State.GetOutputText("agent-1").ShouldBe("Output from agent-1");

        // agent-2 有错误输出
        var agent2Output = result.State.GetOutputText("agent-2");
        agent2Output.ShouldNotBeNull();
        agent2Output.ShouldContain("Error");

        // agent-3 不应执行（fail fast）
        result.State.GetOutputText("agent-3").ShouldBeNull();
        result.StepResults.ShouldNotContain(r => r.StepId == "agent-3");
    }

    #endregion

    #region Helper: InMemoryCheckpointStore

    /// <summary>
    /// 内存检查点存储 - 用于测试
    /// </summary>
    private sealed class InMemoryCheckpointStore : IWorkflowCheckpointStore
    {
        private readonly Dictionary<string, WorkflowCheckpoint> _store = new(StringComparer.OrdinalIgnoreCase);

        public Task SaveCheckpointAsync(WorkflowCheckpoint checkpoint, CancellationToken ct = default)
        {
            _store[checkpoint.ExecutionId] = checkpoint;
            return Task.CompletedTask;
        }

        public Task<WorkflowCheckpoint?> GetCheckpointAsync(string executionId, CancellationToken ct = default)
        {
            _store.TryGetValue(executionId, out var checkpoint);
            return Task.FromResult(checkpoint);
        }

        public Task DeleteCheckpointAsync(string executionId, CancellationToken ct = default)
        {
            _store.Remove(executionId);
            return Task.CompletedTask;
        }
    }

    #endregion
}
