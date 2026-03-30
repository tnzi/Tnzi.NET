using Tnzi.AI.Constants;
using Tnzi.AI.Models;

namespace Tnzi.AI.Tests;

public class TnziAiClientTests
{
    [Fact]
    public async Task ChatAsync_CallsAgentRuntime_ReturnsResponse()
    {
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "Hello from AI",
                ThreadId = Guid.NewGuid(),
                FinishReason = FinishReasons.Stop
            });

        var threadService = new Mock<IAgentThreadService>();
        threadService.Setup(s => s.CreateAsync(It.IsAny<CreateAgentThreadDto>()))
            .ReturnsAsync(Result<AgentThreadDto>.Success(new AgentThreadDto { Id = Guid.NewGuid() }));

        var client = new TnziAiClient(runtime.Object, threadService.Object);

        var response = await client.ChatAsync("Hello");

        Assert.Equal("Hello from AI", response.Text);
        Assert.NotNull(response.ThreadId);
    }

    [Fact]
    public async Task ChatAsync_WithThreadId_PassesToRuntime()
    {
        var threadId = Guid.NewGuid();
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(r => r.RunAsync(It.Is<AgentRunRequest>(req => req.ThreadId == threadId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "Continuing conversation",
                ThreadId = threadId,
                FinishReason = FinishReasons.Stop
            });

        var client = new TnziAiClient(runtime.Object, null);

        var response = await client.ChatAsync("Continue", threadId);

        Assert.Equal(threadId, response.ThreadId);
        runtime.Verify(r => r.RunAsync(
            It.Is<AgentRunRequest>(req => req.ThreadId == threadId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_WithOptions_MapsThem()
    {
        var agentId = Guid.NewGuid();
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(r => r.RunAsync(It.Is<AgentRunRequest>(req => req.AgentId == agentId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Response = "OK", FinishReason = FinishReasons.Stop });

        var client = new TnziAiClient(runtime.Object, null);

        await client.ChatAsync("Hi", options: new AiClientOptions { AgentId = agentId });

        runtime.Verify(r => r.RunAsync(
            It.Is<AgentRunRequest>(req => req.AgentId == agentId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateThreadAsync_CallsThreadService()
    {
        var expectedId = Guid.NewGuid();
        var runtime = new Mock<IAgentRuntime>();
        var threadService = new Mock<IAgentThreadService>();
        threadService.Setup(s => s.CreateAsync(It.IsAny<CreateAgentThreadDto>()))
            .ReturnsAsync(Result<AgentThreadDto>.Success(new AgentThreadDto { Id = expectedId }));

        var client = new TnziAiClient(runtime.Object, threadService.Object);

        var threadId = await client.CreateThreadAsync("Test Thread");

        Assert.Equal(expectedId, threadId);
    }

    [Fact]
    public async Task DeleteThreadAsync_CallsThreadService()
    {
        var threadId = Guid.NewGuid();
        var runtime = new Mock<IAgentRuntime>();
        var threadService = new Mock<IAgentThreadService>();
        threadService.Setup(s => s.DeleteAsync(threadId))
            .ReturnsAsync(Result.Success());

        var client = new TnziAiClient(runtime.Object, threadService.Object);

        await client.DeleteThreadAsync(threadId);

        threadService.Verify(s => s.DeleteAsync(threadId), Times.Once);
    }

    [Fact]
    public async Task ChatStreamingAsync_YieldsChunks()
    {
        var runtime = new Mock<IAgentRuntime>();
        var chunks = new List<AgentStreamChunk>
        {
            new() { Text = "Hello " },
            new() { Text = "world" },
            new() { FinishReason = FinishReasons.Stop }
        };

        runtime.Setup(r => r.RunStreamingAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(chunks));

        var client = new TnziAiClient(runtime.Object, null);

        var events = new List<AiClientStreamEvent>();
        await foreach (var e in client.ChatStreamingAsync("Hi"))
        {
            events.Add(e);
        }

        Assert.Equal(3, events.Count);
        Assert.Equal("Hello ", events[0].Text);
        Assert.Equal("world", events[1].Text);
        Assert.NotNull(events[2].FinishReason);
    }

    [Fact]
    public async Task CreateThreadAsync_ThrowsWhenNoThreadService()
    {
        var runtime = new Mock<IAgentRuntime>();
        var client = new TnziAiClient(runtime.Object, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreateThreadAsync("Test"));
    }

    [Fact]
    public async Task DeleteThreadAsync_ThrowsWhenNoThreadService()
    {
        var runtime = new Mock<IAgentRuntime>();
        var client = new TnziAiClient(runtime.Object, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DeleteThreadAsync(Guid.NewGuid()));
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.CompletedTask;
        }
    }
}
