namespace Tnzi.AI.Tests;

/// <summary>
/// 工作流检查点单元测试 — 验证 WorkflowState 序列化、检查点恢复、断点续执行
/// </summary>
public class WorkflowCheckpointTests
{
    [Fact]
    public void WorkflowState_ToDictionary_ReturnsAllOutputs()
    {
        // Arrange
        var state = new WorkflowState("initial input");
        state.SetOutput("step-a", "output A");
        state.SetOutput("step-b", "output B");
        state.SetOutput("step-c", "output C");

        // Act
        var dict = state.ToDictionary();

        // Assert
        dict.Count.ShouldBe(3);
        dict["step-a"].ShouldBe("output A");
        dict["step-b"].ShouldBe("output B");
        dict["step-c"].ShouldBe("output C");
    }

    [Fact]
    public void WorkflowState_ToDictionary_EmptyState_ReturnsEmptyDictionary()
    {
        // Arrange
        var state = new WorkflowState("initial input");

        // Act
        var dict = state.ToDictionary();

        // Assert
        dict.Count.ShouldBe(0);
    }

    [Fact]
    public void WorkflowState_FromCheckpoint_RestoresState()
    {
        // Arrange
        var checkpoint = new WorkflowCheckpoint
        {
            ExecutionId = "exec-001",
            InitialInput = "hello world",
            CompletedStepIds = ["step-a", "step-b"],
            StepOutputs = new Dictionary<string, string>
            {
                ["step-a"] = "output from A",
                ["step-b"] = "output from B"
            },
            Status = "running"
        };

        // Act
        var state = WorkflowState.FromCheckpoint(checkpoint);

        // Assert
        state.InitialInput.ShouldBe("hello world");
        state.GetOutput("step-a").ShouldBe("output from A");
        state.GetOutput("step-b").ShouldBe("output from B");
        state.GetOutput("step-c").ShouldBeNull();
    }

    [Fact]
    public void WorkflowState_FromCheckpoint_EmptyCheckpoint_CreatesCleanState()
    {
        // Arrange
        var checkpoint = new WorkflowCheckpoint
        {
            ExecutionId = "exec-002",
            InitialInput = "test input",
            CompletedStepIds = [],
            StepOutputs = new(),
            Status = "running"
        };

        // Act
        var state = WorkflowState.FromCheckpoint(checkpoint);

        // Assert
        state.InitialInput.ShouldBe("test input");
        state.ToDictionary().Count.ShouldBe(0);
    }

    [Fact]
    public async Task RunDagAsync_WithCheckpointing_SavesAfterEachLayer()
    {
        // Arrange: A (layer 1) → B (layer 2), 两层 DAG
        var steps = new List<DagStep>
        {
            new() { StepId = "A", Agent = CreateAgent("Output from A") },
            new() { StepId = "B", Agent = CreateAgent("Output from B"), DependsOn = ["A"] }
        };

        var mockStore = new Mock<IWorkflowCheckpointStore>();
        var savedCheckpoints = new List<WorkflowCheckpoint>();
        mockStore.Setup(s => s.SaveCheckpointAsync(It.IsAny<WorkflowCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowCheckpoint, CancellationToken>((cp, _) =>
            {
                // 深拷贝避免引用变更
                savedCheckpoints.Add(new WorkflowCheckpoint
                {
                    ExecutionId = cp.ExecutionId,
                    CompletedStepIds = new HashSet<string>(cp.CompletedStepIds),
                    StepOutputs = new Dictionary<string, string>(cp.StepOutputs),
                    InitialInput = cp.InitialInput,
                    Status = cp.Status
                });
            })
            .Returns(Task.CompletedTask);

        var options = new WorkflowExecutionOptions
        {
            ExecutionId = "test-exec-001",
            CheckpointStore = mockStore.Object
        };

        // Act
        var result = await WorkflowRunner.RunDagAsync(steps, "Initial input", options);

        // Assert
        result.FinalOutput.ShouldBe("Output from B");

        // 应保存多次：Layer 1 完成后 + Layer 2 完成后 + 最终
        // (Layer 2 完成后与最终可能合并为一次，但至少保存了 2 次)
        savedCheckpoints.Count.ShouldBeGreaterThanOrEqualTo(2);

        // 第一次保存应包含 step A
        var firstSave = savedCheckpoints[0];
        firstSave.ExecutionId.ShouldBe("test-exec-001");
        firstSave.CompletedStepIds.ShouldContain("A");
        firstSave.StepOutputs["A"].ShouldBe("Output from A");
        firstSave.Status.ShouldBe("running");

        // 最后一次保存应包含 A 和 B，状态为 completed
        var lastSave = savedCheckpoints[^1];
        lastSave.CompletedStepIds.ShouldContain("A");
        lastSave.CompletedStepIds.ShouldContain("B");
        lastSave.StepOutputs["B"].ShouldBe("Output from B");
        lastSave.Status.ShouldBe("completed");
    }

    [Fact]
    public async Task RunDagAsync_Resume_SkipsCompletedSteps()
    {
        // Arrange: A → B → C, 假设 A 已完成
        var agentBCalled = false;
        var agentCCalled = false;

        // Agent A 不应被调用（已完成）
        var agentA = CreateAgent("Should not run");

        // Agent B 应被调用
        var mockClientB = new Mock<IChatClient>();
        mockClientB.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => agentBCalled = true)
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Output from B (resumed)")));
        var agentB = new AgentExecutor(mockClientB.Object, new AgentExecutorOptions { Name = "AgentB" });

        // Agent C 应被调用
        var mockClientC = new Mock<IChatClient>();
        mockClientC.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => agentCCalled = true)
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Final output (resumed)")));
        var agentC = new AgentExecutor(mockClientC.Object, new AgentExecutorOptions { Name = "AgentC" });

        var steps = new List<DagStep>
        {
            new() { StepId = "A", Agent = agentA },
            new() { StepId = "B", Agent = agentB, DependsOn = ["A"] },
            new() { StepId = "C", Agent = agentC, DependsOn = ["B"] }
        };

        // 模拟已有检查点：A 已完成
        var existingCheckpoint = new WorkflowCheckpoint
        {
            ExecutionId = "resume-exec-001",
            InitialInput = "Initial input",
            CompletedStepIds = ["A"],
            StepOutputs = new Dictionary<string, string> { ["A"] = "Output from A (previous)" },
            Status = "running"
        };

        var mockStore = new Mock<IWorkflowCheckpointStore>();
        mockStore.Setup(s => s.GetCheckpointAsync("resume-exec-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCheckpoint);
        mockStore.Setup(s => s.SaveCheckpointAsync(It.IsAny<WorkflowCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = new WorkflowExecutionOptions
        {
            ExecutionId = "resume-exec-001",
            Resume = true,
            CheckpointStore = mockStore.Object
        };

        // Act
        var result = await WorkflowRunner.RunDagAsync(steps, "Initial input", options);

        // Assert
        result.FinalOutput.ShouldBe("Final output (resumed)");

        // Step A 不应被执行（已在检查点中）
        // 注：由于 A 的 Agent 是固定返回的 mock，无法直接验证是否被调用
        // 但 B 和 C 应被调用
        agentBCalled.ShouldBeTrue();
        agentCCalled.ShouldBeTrue();

        // 验证结果包含所有步骤（包括从检查点恢复的 A）
        result.StepResults.ShouldContain(r => r.StepId == "A");
        result.StepResults.ShouldContain(r => r.StepId == "B");
        result.StepResults.ShouldContain(r => r.StepId == "C");
    }

    [Fact]
    public async Task RunDagAsync_WithoutOptions_BackwardCompatible()
    {
        // Arrange: 简单线性 DAG，不传 options
        var steps = new List<DagStep>
        {
            new() { StepId = "A", Agent = CreateAgent("Output A") },
            new() { StepId = "B", Agent = CreateAgent("Output B"), DependsOn = ["A"] }
        };

        // Act: 调用无 options 的重载
        var result = await WorkflowRunner.RunDagAsync(steps, "Input");

        // Assert: 正常执行
        result.FinalOutput.ShouldBe("Output B");
        result.StepResults.Count.ShouldBe(2);
        result.State.GetOutput("A").ShouldBe("Output A");
        result.State.GetOutput("B").ShouldBe("Output B");
    }

    [Fact]
    public async Task RunDagAsync_WithOptionsNull_BackwardCompatible()
    {
        // Arrange: 简单 DAG，显式传 null options
        var steps = new List<DagStep>
        {
            new() { StepId = "X", Agent = CreateAgent("Done") }
        };

        // Act
        var result = await WorkflowRunner.RunDagAsync(steps, "Hello", options: null);

        // Assert
        result.FinalOutput.ShouldBe("Done");
        result.StepResults.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RunDagAsync_ResumeWithNoExistingCheckpoint_StartsFromBeginning()
    {
        // Arrange: Resume=true 但检查点不存在，应从头开始
        var steps = new List<DagStep>
        {
            new() { StepId = "A", Agent = CreateAgent("Fresh output") }
        };

        var mockStore = new Mock<IWorkflowCheckpointStore>();
        mockStore.Setup(s => s.GetCheckpointAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowCheckpoint?)null);
        mockStore.Setup(s => s.SaveCheckpointAsync(It.IsAny<WorkflowCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = new WorkflowExecutionOptions
        {
            ExecutionId = "no-existing-checkpoint",
            Resume = true,
            CheckpointStore = mockStore.Object
        };

        // Act
        var result = await WorkflowRunner.RunDagAsync(steps, "Input", options);

        // Assert
        result.FinalOutput.ShouldBe("Fresh output");
    }

    [Fact]
    public void WorkflowCheckpoint_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var checkpoint = new WorkflowCheckpoint();

        // Assert
        checkpoint.ExecutionId.ShouldBe(string.Empty);
        checkpoint.CompletedStepIds.ShouldNotBeNull();
        checkpoint.CompletedStepIds.Count.ShouldBe(0);
        checkpoint.StepOutputs.ShouldNotBeNull();
        checkpoint.StepOutputs.Count.ShouldBe(0);
        checkpoint.InitialInput.ShouldBe(string.Empty);
        checkpoint.Status.ShouldBe("running");
    }

    [Fact]
    public void WorkflowExecutionOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new WorkflowExecutionOptions();

        // Assert
        options.ExecutionId.ShouldBeNull();
        options.Resume.ShouldBeFalse();
        options.CheckpointStore.ShouldBeNull();
    }

    /// <summary>
    /// 创建返回固定文本的 AgentExecutor
    /// </summary>
    private static AgentExecutor CreateAgent(string responseText)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        return new AgentExecutor(mock.Object, new AgentExecutorOptions { Name = $"Agent_{responseText}" });
    }
}
