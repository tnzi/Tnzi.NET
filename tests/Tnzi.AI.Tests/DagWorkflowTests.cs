namespace Tnzi.AI.Tests;

/// <summary>
/// WorkflowRunner DAG 执行测试 — 验证拓扑排序、并行执行、条件分支
/// </summary>
public class DagWorkflowTests
{
    [Fact]
    public async Task RunDagAsync_LinearChain_ExecutesInOrder()
    {
        // A → B → C
        var steps = new List<DagStep>
        {
            new() { StepId = "A", Agent = CreateAgent("Output from A") },
            new() { StepId = "B", Agent = CreateAgent("Output from B"), DependsOn = ["A"] },
            new() { StepId = "C", Agent = CreateAgent("Final output"), DependsOn = ["B"] }
        };

        var result = await WorkflowRunner.RunDagAsync(steps, "Initial input");

        result.FinalOutput.ShouldBe("Final output");
        result.StepResults.Count.ShouldBe(3);
        result.State.GetOutput("A").ShouldBe("Output from A");
        result.State.GetOutput("B").ShouldBe("Output from B");
        result.State.GetOutput("C").ShouldBe("Final output");
    }

    [Fact]
    public async Task RunDagAsync_ParallelSteps_AllExecute()
    {
        // A and B have no dependencies (run in parallel), C depends on both
        var steps = new List<DagStep>
        {
            new() { StepId = "A", Agent = CreateAgent("Result A") },
            new() { StepId = "B", Agent = CreateAgent("Result B") },
            new() { StepId = "C", Agent = CreateAgent("Combined"), DependsOn = ["A", "B"] }
        };

        var result = await WorkflowRunner.RunDagAsync(steps, "Input");

        result.FinalOutput.ShouldBe("Combined");
        result.StepResults.Count.ShouldBe(3);
        result.State.GetOutput("A").ShouldBe("Result A");
        result.State.GetOutput("B").ShouldBe("Result B");
    }

    [Fact]
    public async Task RunDagAsync_ConditionalStep_SkipsWhenFalse()
    {
        var steps = new List<DagStep>
        {
            new() { StepId = "A", Agent = CreateAgent("go") },
            new() { StepId = "B", Agent = CreateAgent("Should not run"), Condition = "false", DependsOn = ["A"] },
            new() { StepId = "C", Agent = CreateAgent("Final"), DependsOn = ["A"] }
        };

        var result = await WorkflowRunner.RunDagAsync(steps, "Input");

        result.FinalOutput.ShouldBe("Final");
        // Step B should be skipped (output empty or null)
        var stepBOutput = result.State.GetOutput("B");
        (string.IsNullOrEmpty(stepBOutput)).ShouldBeTrue();
        result.State.GetOutput("C").ShouldBe("Final");
    }

    [Fact]
    public async Task RunDagAsync_ConditionalStep_ExecutesWhenTrue()
    {
        var steps = new List<DagStep>
        {
            new() { StepId = "A", Agent = CreateAgent("proceed") },
            new() { StepId = "B", Agent = CreateAgent("Conditional ran"), Condition = "proceed", DependsOn = ["A"] }
        };

        var result = await WorkflowRunner.RunDagAsync(steps, "Input");

        result.State.GetOutput("B").ShouldBe("Conditional ran");
    }

    [Fact]
    public async Task RunDagAsync_SingleStep_UsesInitialInput()
    {
        var steps = new List<DagStep>
        {
            new() { StepId = "only", Agent = CreateAgent("Done") }
        };

        var result = await WorkflowRunner.RunDagAsync(steps, "Hello");

        result.FinalOutput.ShouldBe("Done");
        result.StepResults.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RunDagAsync_EmptySteps_ThrowsArgumentException()
    {
        await Should.ThrowAsync<ArgumentException>(async () =>
            await WorkflowRunner.RunDagAsync([], "Hello"));
    }

    [Fact]
    public async Task WorkflowState_ResolveTemplate_ReplacesPlaceholders()
    {
        var state = new WorkflowState("Initial");
        state.SetOutput("step1", "Hello");
        state.SetOutput("step2", "World");

        var resolved = state.ResolveTemplate("{{step1}} {{step2}}!");
        resolved.ShouldBe("Hello World!");
    }

    [Fact]
    public async Task WorkflowState_ResolveTemplate_UnknownPlaceholder_KeepsOriginal()
    {
        var state = new WorkflowState("Initial");
        state.SetOutput("step1", "Hello");

        var resolved = state.ResolveTemplate("{{step1}} {{unknown}}");
        resolved.ShouldContain("Hello");
        resolved.ShouldContain("{{unknown}}");
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
