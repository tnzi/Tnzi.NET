namespace Tnzi.AI.Tests;

/// <summary>
/// 工作流 Human-in-the-Loop (HITL) 单元测试 — 验证审批暂停、恢复、拒绝、修改输入
/// </summary>
public class WorkflowHitlTests
{
    [Fact]
    public async Task RunDagAsync_StepWithRequiresApproval_PausesWorkflow_WhenNoHandler()
    {
        // Arrange: A → B (requires approval) → C
        var steps = new List<DagStep>
        {
            new() { StepId = "A", Agent = CreateAgent("Output from A") },
            new() { StepId = "B", Agent = CreateAgent("Output from B"), DependsOn = ["A"], RequiresApproval = true },
            new() { StepId = "C", Agent = CreateAgent("Output from C"), DependsOn = ["B"] }
        };

        var savedCheckpoints = new List<WorkflowCheckpoint>();
        var mockStore = CreateMockStore(savedCheckpoints);

        var options = new WorkflowExecutionOptions
        {
            ExecutionId = "hitl-pause-001",
            CheckpointStore = mockStore.Object
            // InterruptHandler 为 null → 应暂停并保存检查点
        };

        // Act
        var result = await WorkflowRunner.RunDagAsync(steps, "Initial input", options);

        // Assert: 工作流应暂停
        result.AwaitingApproval.ShouldBeTrue();
        result.AwaitingApprovalStepId.ShouldBe("B");

        // A 和 B 应已完成，C 不应执行
        result.StepResults.ShouldContain(r => r.StepId == "A");
        result.StepResults.ShouldContain(r => r.StepId == "B");
        result.StepResults.ShouldNotContain(r => r.StepId == "C");

        // 检查点应有 awaiting_approval 状态
        savedCheckpoints.ShouldContain(cp => cp.Status == "awaiting_approval");
        var awaitingCp = savedCheckpoints.First(cp => cp.Status == "awaiting_approval");
        awaitingCp.StepsAwaitingApproval.ShouldContain("B");
        awaitingCp.CompletedStepIds.ShouldContain("A");
        awaitingCp.CompletedStepIds.ShouldContain("B");
    }

    [Fact]
    public async Task RunDagAsync_ResumeAfterApproval_ContinuesExecution()
    {
        // Arrange: A → B (approved) → C
        // 模拟第一次运行在 B 处暂停，现在恢复执行
        var agentCCalled = false;

        var mockClientC = new Mock<IChatClient>();
        mockClientC.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => agentCCalled = true)
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Final output from C")));
        var agentC = new AgentExecutor(mockClientC.Object, new AgentExecutorOptions { Name = "AgentC" });

        var steps = new List<DagStep>
        {
            new() { StepId = "A", Agent = CreateAgent("Output from A") },
            new() { StepId = "B", Agent = CreateAgent("Output from B"), DependsOn = ["A"], RequiresApproval = true },
            new() { StepId = "C", Agent = agentC, DependsOn = ["B"] }
        };

        // 模拟已有检查点：A 和 B 都已完成（审批通过后的状态）
        var existingCheckpoint = new WorkflowCheckpoint
        {
            ExecutionId = "hitl-resume-001",
            InitialInput = "Initial input",
            CompletedStepIds = ["A", "B"],
            StepOutputs = new Dictionary<string, string>
            {
                ["A"] = "Output from A",
                ["B"] = "Output from B"
            },
            Status = "running" // 审批通过后状态改为 running
        };

        var mockStore = new Mock<IWorkflowCheckpointStore>();
        mockStore.Setup(s => s.GetCheckpointAsync("hitl-resume-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCheckpoint);
        mockStore.Setup(s => s.SaveCheckpointAsync(It.IsAny<WorkflowCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = new WorkflowExecutionOptions
        {
            ExecutionId = "hitl-resume-001",
            Resume = true,
            CheckpointStore = mockStore.Object
        };

        // Act
        var result = await WorkflowRunner.RunDagAsync(steps, "Initial input", options);

        // Assert
        result.AwaitingApproval.ShouldBeFalse();
        result.FinalOutput.ShouldBe("Final output from C");
        agentCCalled.ShouldBeTrue();
        result.StepResults.ShouldContain(r => r.StepId == "C");
    }

    [Fact]
    public async Task RunDagAsync_WithInterruptHandler_Approved_ContinuesExecution()
    {
        // Arrange: A → B (requires approval, handler approves) → C
        var steps = new List<DagStep>
        {
            new() { StepId = "A", Agent = CreateAgent("Output from A") },
            new() { StepId = "B", Agent = CreateAgent("Output from B"), DependsOn = ["A"], RequiresApproval = true },
            new() { StepId = "C", Agent = CreateAgent("Output from C"), DependsOn = ["B"] }
        };

        var mockHandler = new Mock<IWorkflowInterruptHandler>();
        mockHandler.Setup(h => h.HandleInterruptAsync(It.IsAny<WorkflowInterruptContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowInterruptResult { Approved = true });

        var options = new WorkflowExecutionOptions
        {
            ExecutionId = "hitl-approve-001",
            InterruptHandler = mockHandler.Object
        };

        // Act
        var result = await WorkflowRunner.RunDagAsync(steps, "Initial input", options);

        // Assert: 工作流应完成（审批通过）
        result.AwaitingApproval.ShouldBeFalse();
        result.FinalOutput.ShouldBe("Output from C");
        result.StepResults.Count.ShouldBe(3);

        // 中断处理器应被调用一次（步骤 B）
        mockHandler.Verify(h => h.HandleInterruptAsync(
            It.Is<WorkflowInterruptContext>(ctx => ctx.StepId == "B" && ctx.StepOutput == "Output from B"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunDagAsync_WithInterruptHandler_ModifiedInput_UsesModifiedValue()
    {
        // Arrange: A → B (requires approval, handler modifies) → C
        var steps = new List<DagStep>
        {
            new() { StepId = "A", Agent = CreateAgent("Output from A") },
            new() { StepId = "B", Agent = CreateAgent("Original output from B"), DependsOn = ["A"], RequiresApproval = true },
            new() { StepId = "C", Agent = CreateAgent("Output from C"), DependsOn = ["B"] }
        };

        var mockHandler = new Mock<IWorkflowInterruptHandler>();
        mockHandler.Setup(h => h.HandleInterruptAsync(It.IsAny<WorkflowInterruptContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowInterruptResult
            {
                Approved = true,
                ModifiedInput = "Human-modified output for B"
            });

        var options = new WorkflowExecutionOptions
        {
            ExecutionId = "hitl-modify-001",
            InterruptHandler = mockHandler.Object
        };

        // Act
        var result = await WorkflowRunner.RunDagAsync(steps, "Initial input", options);

        // Assert
        result.AwaitingApproval.ShouldBeFalse();

        // B 的输出应被修改
        result.State.GetOutput("B").ShouldBe("Human-modified output for B");

        // stepResults 中 B 的输出也应更新
        var stepB = result.StepResults.FirstOrDefault(r => r.StepId == "B");
        stepB.ShouldNotBeNull();
        stepB!.Output.ShouldBe("Human-modified output for B");
    }

    [Fact]
    public async Task RunDagAsync_WithInterruptHandler_Rejected_StopsWorkflow()
    {
        // Arrange: A → B (requires approval, handler rejects) → C
        var steps = new List<DagStep>
        {
            new() { StepId = "A", Agent = CreateAgent("Output from A") },
            new() { StepId = "B", Agent = CreateAgent("Output from B"), DependsOn = ["A"], RequiresApproval = true },
            new() { StepId = "C", Agent = CreateAgent("Should not run"), DependsOn = ["B"] }
        };

        var mockHandler = new Mock<IWorkflowInterruptHandler>();
        mockHandler.Setup(h => h.HandleInterruptAsync(It.IsAny<WorkflowInterruptContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowInterruptResult
            {
                Approved = false,
                Feedback = "Content not appropriate"
            });

        var savedCheckpoints = new List<WorkflowCheckpoint>();
        var mockStore = CreateMockStore(savedCheckpoints);

        var options = new WorkflowExecutionOptions
        {
            ExecutionId = "hitl-reject-001",
            InterruptHandler = mockHandler.Object,
            CheckpointStore = mockStore.Object
        };

        // Act
        var result = await WorkflowRunner.RunDagAsync(steps, "Initial input", options);

        // Assert: 工作流应标记为失败（拒绝）
        result.AwaitingApproval.ShouldBeFalse();

        // B 的输出应包含拒绝信息
        var stepB = result.StepResults.FirstOrDefault(r => r.StepId == "B");
        stepB.ShouldNotBeNull();
        stepB!.Output.ShouldContain("[Rejected:");
        stepB.Output.ShouldContain("Content not appropriate");

        // 最终检查点应为 failed 状态
        savedCheckpoints.ShouldContain(cp => cp.Status == "failed");
    }

    [Fact]
    public async Task RunDagAsync_NoRequiresApproval_NormalExecution()
    {
        // Arrange: 没有步骤需要审批，应该正常执行
        var steps = new List<DagStep>
        {
            new() { StepId = "A", Agent = CreateAgent("Output from A") },
            new() { StepId = "B", Agent = CreateAgent("Output from B"), DependsOn = ["A"] }
        };

        var options = new WorkflowExecutionOptions
        {
            ExecutionId = "hitl-normal-001"
        };

        // Act
        var result = await WorkflowRunner.RunDagAsync(steps, "Input", options);

        // Assert
        result.AwaitingApproval.ShouldBeFalse();
        result.AwaitingApprovalStepId.ShouldBeNull();
        result.FinalOutput.ShouldBe("Output from B");
        result.StepResults.Count.ShouldBe(2);
    }

    [Fact]
    public async Task RunDagAsync_RequiresApproval_NoStoreNoHandler_ContinuesExecution()
    {
        // Arrange: 需要审批但没有检查点存储也没有中断处理器 → 忽略审批，继续执行
        var steps = new List<DagStep>
        {
            new() { StepId = "A", Agent = CreateAgent("Output from A"), RequiresApproval = true },
            new() { StepId = "B", Agent = CreateAgent("Output from B"), DependsOn = ["A"] }
        };

        var options = new WorkflowExecutionOptions
        {
            ExecutionId = "hitl-nostore-001"
            // 无 CheckpointStore 和 InterruptHandler
        };

        // Act
        var result = await WorkflowRunner.RunDagAsync(steps, "Input", options);

        // Assert: 应该正常完成（忽略审批要求）
        result.AwaitingApproval.ShouldBeFalse();
        result.FinalOutput.ShouldBe("Output from B");
        result.StepResults.Count.ShouldBe(2);
    }

    [Fact]
    public void DagStep_RequiresApproval_DefaultsFalse()
    {
        var step = new DagStep();
        step.RequiresApproval.ShouldBeFalse();
    }

    [Fact]
    public void WorkflowInterruptContext_DefaultValues()
    {
        var context = new WorkflowInterruptContext();
        context.ExecutionId.ShouldBe(string.Empty);
        context.StepId.ShouldBe(string.Empty);
        context.AgentName.ShouldBe(string.Empty);
        context.StepOutput.ShouldBe(string.Empty);
    }

    [Fact]
    public void WorkflowInterruptResult_DefaultValues()
    {
        var result = new WorkflowInterruptResult();
        result.Approved.ShouldBeFalse();
        result.Feedback.ShouldBeNull();
        result.ModifiedInput.ShouldBeNull();
    }

    [Fact]
    public void WorkflowCheckpoint_StepsAwaitingApproval_DefaultsToEmpty()
    {
        var checkpoint = new WorkflowCheckpoint();
        checkpoint.StepsAwaitingApproval.ShouldNotBeNull();
        checkpoint.StepsAwaitingApproval.Count.ShouldBe(0);
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

    /// <summary>
    /// 创建 Mock 检查点存储，捕获所有保存的检查点
    /// </summary>
    private static Mock<IWorkflowCheckpointStore> CreateMockStore(List<WorkflowCheckpoint> savedCheckpoints)
    {
        var mockStore = new Mock<IWorkflowCheckpointStore>();
        mockStore.Setup(s => s.SaveCheckpointAsync(It.IsAny<WorkflowCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowCheckpoint, CancellationToken>((cp, _) =>
            {
                savedCheckpoints.Add(new WorkflowCheckpoint
                {
                    ExecutionId = cp.ExecutionId,
                    CompletedStepIds = new HashSet<string>(cp.CompletedStepIds),
                    StepOutputs = new Dictionary<string, string>(cp.StepOutputs),
                    InitialInput = cp.InitialInput,
                    Status = cp.Status,
                    StepsAwaitingApproval = new HashSet<string>(cp.StepsAwaitingApproval)
                });
            })
            .Returns(Task.CompletedTask);
        return mockStore;
    }
}
