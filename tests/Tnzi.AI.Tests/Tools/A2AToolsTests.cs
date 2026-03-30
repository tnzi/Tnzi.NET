namespace Tnzi.AI.Tests.Tools;

public class A2AToolsTests
{
    private readonly Mock<IA2AClient> _mockClient;
    private readonly A2ATools _tools;

    public A2AToolsTests()
    {
        _mockClient = new Mock<IA2AClient>();
        var logger = NullLoggerFactory.Instance.CreateLogger<A2ATools>();
        _tools = new A2ATools(logger, _mockClient.Object) { PollIntervalMs = 1 };
    }

    [Fact]
    public async Task InvokeAgentAsync_SuccessfulTask_ReturnsOutput()
    {
        _mockClient.Setup(c => c.SendTaskAsync(
                It.IsAny<string>(), It.IsAny<A2ATaskRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new A2AResponse { TaskId = "t1", Status = "completed", Output = "Hello from remote" });

        var result = await _tools.InvokeAgentAsync("https://remote.example.com", "Say hello");

        result.ShouldContain("Hello from remote");
    }

    [Fact]
    public async Task InvokeAgentAsync_FailedTask_ReturnsError()
    {
        _mockClient.Setup(c => c.SendTaskAsync(
                It.IsAny<string>(), It.IsAny<A2ATaskRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new A2AResponse { TaskId = "t1", Status = "failed", Error = "Connection refused" });

        var result = await _tools.InvokeAgentAsync("https://remote.example.com", "Do something");

        result.ShouldContain("failed");
        result.ShouldContain("Connection refused");
    }

    [Fact]
    public async Task InvokeAgentAsync_PendingTask_PollsUntilComplete()
    {
        var callCount = 0;
        _mockClient.Setup(c => c.SendTaskAsync(
                It.IsAny<string>(), It.IsAny<A2ATaskRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new A2AResponse { TaskId = "t1", Status = "pending" });

        _mockClient.Setup(c => c.GetTaskStatusAsync(
                It.IsAny<string>(), "t1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount >= 2
                    ? new A2AResponse { TaskId = "t1", Status = "completed", Output = "Done after polling" }
                    : new A2AResponse { TaskId = "t1", Status = "running" };
            });

        var result = await _tools.InvokeAgentAsync("https://remote.example.com", "Long task");

        result.ShouldContain("Done after polling");
        callCount.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task InvokeAgentAsync_NoClient_ReturnsUnavailable()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger<A2ATools>();
        var tools = new A2ATools(logger);

        var result = await tools.InvokeAgentAsync("https://example.com", "test");

        result.ShouldContain("unavailable");
    }

    [Fact]
    public async Task InvokeAgentAsync_ClientThrows_ReturnsError()
    {
        _mockClient.Setup(c => c.SendTaskAsync(
                It.IsAny<string>(), It.IsAny<A2ATaskRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        var result = await _tools.InvokeAgentAsync("https://remote.example.com", "test");

        result.ShouldContain("error");
        result.ShouldContain("Network error");
    }
}
