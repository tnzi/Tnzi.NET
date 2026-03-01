namespace Tnzi.AI.Tests;

/// <summary>
/// WorkflowRunner 流式执行单元测试
/// </summary>
public class WorkflowStreamingTests
{
    [Fact]
    public async Task RunSequentialStreamingAsync_SingleAgent_StreamsProgress()
    {
        var agent = CreateStreamingAgent("AgentA", ["Hello", " World"]);
        var agents = new List<AgentExecutor> { agent };

        var progress = new List<WorkflowStepProgress>();
        await foreach (var p in WorkflowRunner.RunSequentialStreamingAsync(agents, "Hi"))
        {
            progress.Add(p);
        }

        // 应该有：Started + 2个Streaming + Completed
        progress.Count.ShouldBeGreaterThanOrEqualTo(3);
        progress[0].Status.ShouldBe(WorkflowStepStatus.Started);
        progress[0].StepId.ShouldBe("step-1");
        progress[0].AgentName.ShouldBe("AgentA");

        var streamingChunks = progress.Where(p => p.Status == WorkflowStepStatus.Streaming).ToList();
        streamingChunks.Count.ShouldBe(2);
        streamingChunks[0].Text.ShouldBe("Hello");
        streamingChunks[1].Text.ShouldBe(" World");

        var completed = progress.Last(p => p.Status == WorkflowStepStatus.Completed);
        completed.Output.ShouldBe("Hello World");
    }

    [Fact]
    public async Task RunSequentialStreamingAsync_MultipleAgents_ChainsProperly()
    {
        var agent1 = CreateStreamingAgent("Agent1", ["Step1"]);
        var agent2 = CreateStreamingAgent("Agent2", ["Step2"]);
        var agents = new List<AgentExecutor> { agent1, agent2 };

        var progress = new List<WorkflowStepProgress>();
        await foreach (var p in WorkflowRunner.RunSequentialStreamingAsync(agents, "Input"))
        {
            progress.Add(p);
        }

        // 应该有两组 Started + Streaming + Completed
        var step1Events = progress.Where(p => p.StepId == "step-1").ToList();
        var step2Events = progress.Where(p => p.StepId == "step-2").ToList();

        step1Events.ShouldNotBeEmpty();
        step2Events.ShouldNotBeEmpty();

        step1Events.First().Status.ShouldBe(WorkflowStepStatus.Started);
        step2Events.First().Status.ShouldBe(WorkflowStepStatus.Started);
    }

    [Fact]
    public async Task RunSequentialStreamingAsync_EmptyAgents_Throws()
    {
        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in WorkflowRunner.RunSequentialStreamingAsync([], "Hi"))
            {
            }
        });
    }

    [Fact]
    public async Task RunDagStreamingAsync_SingleStep_StreamsProgress()
    {
        var agent = CreateStreamingAgent("DagAgent", ["Result"]);
        var steps = new List<DagStep>
        {
            new() { StepId = "s1", Agent = agent }
        };

        var progress = new List<WorkflowStepProgress>();
        await foreach (var p in WorkflowRunner.RunDagStreamingAsync(steps, "Input"))
        {
            progress.Add(p);
        }

        progress.ShouldNotBeEmpty();
        progress.ShouldContain(p => p.Status == WorkflowStepStatus.Started && p.StepId == "s1");
        progress.ShouldContain(p => p.Status == WorkflowStepStatus.Completed && p.StepId == "s1");
    }

    [Fact]
    public async Task RunDagStreamingAsync_WithDependencies_ExecutesInOrder()
    {
        var agent1 = CreateStreamingAgent("Agent1", ["A"]);
        var agent2 = CreateStreamingAgent("Agent2", ["B"]);

        var steps = new List<DagStep>
        {
            new() { StepId = "s1", Agent = agent1 },
            new() { StepId = "s2", Agent = agent2, DependsOn = ["s1"] }
        };

        var progress = new List<WorkflowStepProgress>();
        await foreach (var p in WorkflowRunner.RunDagStreamingAsync(steps, "Input"))
        {
            progress.Add(p);
        }

        // s1 的 Completed 应该在 s2 的 Started 之前
        var s1Complete = progress.FindIndex(p => p.StepId == "s1" && p.Status == WorkflowStepStatus.Completed);
        var s2Start = progress.FindIndex(p => p.StepId == "s2" && p.Status == WorkflowStepStatus.Started);

        s1Complete.ShouldBeGreaterThanOrEqualTo(0);
        s2Start.ShouldBeGreaterThanOrEqualTo(0);
        s1Complete.ShouldBeLessThan(s2Start);
    }

    [Fact]
    public async Task RunDagStreamingAsync_CyclicDependency_Throws()
    {
        var agent = CreateStreamingAgent("Agent", ["X"]);
        var steps = new List<DagStep>
        {
            new() { StepId = "s1", Agent = agent, DependsOn = ["s2"] },
            new() { StepId = "s2", Agent = agent, DependsOn = ["s1"] }
        };

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in WorkflowRunner.RunDagStreamingAsync(steps, "Input"))
            {
            }
        });
    }

    /// <summary>
    /// 创建返回流式文本 chunk 的 AgentExecutor
    /// </summary>
    private static AgentExecutor CreateStreamingAgent(string name, string[] textChunks)
    {
        var mock = new Mock<IChatClient>();

        // 非流式（Sequential 内部也用非流式来传递输出）
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Join("", textChunks))));

        // 流式
        mock.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(textChunks));

        return new AgentExecutor(mock.Object, new AgentExecutorOptions { Name = name });
    }

    private static IAsyncEnumerable<ChatResponseUpdate> CreateAsyncEnumerable(string[] textChunks)
    {
        return AsyncEnumerable(textChunks);

        static async IAsyncEnumerable<ChatResponseUpdate> AsyncEnumerable(string[] chunks)
        {
            foreach (var chunk in chunks)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent(chunk)]
                };
            }
        }
    }
}
