using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// ToolInterruptBehavior 测试 - 验证 Cancel 和 GracefulShutdown 的取消传播行为
/// </summary>
/// <remarks>
/// ★两种模式的差别直接看「外部 <c>Cancel()</c> 返回的那一刻，工具手里的 token 是不是已经取消」，
/// 而不是用挂钟量工具多久收到取消：挂钟把工具开始到外部发出取消之间的调度延迟也算了进去，
/// 满载机器上光这一段就是好几秒，量到的是机器有多忙。取消回调在 <c>Cancel()</c> 里同步执行，
/// 所以这个时刻的状态是确定的；取消之前先等工具真的开始执行，否则观察的根本不是工具。
/// </remarks>
public class ToolInterruptBehaviorTests
{
    /// <summary>等工具真的开始执行的上限。条件一成立立即返回，按满载机器定。</summary>
    private static readonly TimeSpan ToolStartBound = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task GracefulShutdown_GivesGracePeriodAfterCancellation()
    {
        // Arrange: 工具把自己拿到的 token 交出来，测试据此观察宽限期
        var toolStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);

        var tool = AIFunctionFactory.Create(async (CancellationToken ct) =>
        {
            toolStarted.TrySetResult(ct);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException)
            {
                // 宽限期到了 token 才会取消，走到这里说明宽限期已经用完。
                return "cleanup_done";
            }
            return "completed_normally";
        }, "graceful_tool");

        var toolDefs = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["graceful_tool"] = new()
            {
                Name = "graceful_tool",
                InterruptBehavior = ToolInterruptBehavior.GracefulShutdown,
                IsConcurrencySafe = true
            }
        };

        // 用一个简单的 mock ChatClient 触发工具调用
        var toolCallContent = new FunctionCallContent("call_1", "graceful_tool");
        var assistantMessage = new ChatMessage(ChatRole.Assistant, [toolCallContent]);
        var chatResponse = new ChatResponse([assistantMessage]);

        var callCount = 0;
        var mockClient = new MockChatClient(messages =>
        {
            callCount++;
            if (callCount == 1)
                return new ChatResponse([new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_1", "graceful_tool")])]);
            return new ChatResponse([new ChatMessage(ChatRole.Assistant, "Done")]);
        });

        var options = new AgentExecutorOptions
        {
            Name = "test-agent",
            Tools = [tool],
            ToolDefinitions = toolDefs,
            MaxToolIterations = 3,
            ToolTimeoutSeconds = 30
        };

        var executor = new AgentExecutor(mockClient, options);
        var cts = new CancellationTokenSource();
        var messages = new List<ChatMessage> { new(ChatRole.User, "test") };

        // Act: 等工具真的开始执行再取消
        var executeTask = executor.ExecuteAsync(messages, cts.Token);
        var toolToken = await WaitForToolStartAsync(toolStarted, executeTask);
        cts.Cancel();

        // Assert: 外部已取消，工具的 token 还没有：宽限期在生效
        toolToken.IsCancellationRequested.ShouldBeFalse("GracefulShutdown must give the tool a grace period after cancellation");

        // 宽限期过后取消照样传播：整个 ExecuteAsync 抛出，工具的 token 也已取消
        await Should.ThrowAsync<OperationCanceledException>(async () => await executeTask);
        toolToken.IsCancellationRequested.ShouldBeTrue("the grace period must end in cancellation");
    }

    [Fact]
    public async Task Cancel_PropagatesImmediately()
    {
        // Arrange: 工具把自己拿到的 token 交出来，测试据此观察取消是否立即传播
        var toolStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);

        var tool = AIFunctionFactory.Create(async (CancellationToken ct) =>
        {
            toolStarted.TrySetResult(ct);
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return "should_not_reach";
        }, "cancel_tool");

        var toolDefs = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["cancel_tool"] = new()
            {
                Name = "cancel_tool",
                InterruptBehavior = ToolInterruptBehavior.Cancel, // 默认行为
                IsConcurrencySafe = true
            }
        };

        var callCount = 0;
        var mockClient = new MockChatClient(messages =>
        {
            callCount++;
            if (callCount == 1)
                return new ChatResponse([new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_1", "cancel_tool")])]);
            return new ChatResponse([new ChatMessage(ChatRole.Assistant, "Done")]);
        });

        var options = new AgentExecutorOptions
        {
            Name = "test-agent",
            Tools = [tool],
            ToolDefinitions = toolDefs,
            MaxToolIterations = 3,
            ToolTimeoutSeconds = 30
        };

        var executor = new AgentExecutor(mockClient, options);
        var cts = new CancellationTokenSource();
        var messages = new List<ChatMessage> { new(ChatRole.User, "test") };

        // Act: 等工具真的开始执行再取消
        var executeTask = executor.ExecuteAsync(messages, cts.Token);
        var toolToken = await WaitForToolStartAsync(toolStarted, executeTask);
        cts.Cancel();

        // Assert: Cancel() 一返回，工具的 token 就已取消，没有任何宽限期
        toolToken.IsCancellationRequested.ShouldBeTrue("Cancel mode should propagate cancellation immediately without grace period");
        await Should.ThrowAsync<OperationCanceledException>(async () => await executeTask);
    }

    /// <summary>
    /// 等工具开始执行，返回它拿到的 token。代理运行先结束（工具根本没被调用）立即判失败，
    /// 不必等满上限。
    /// </summary>
    private static async Task<CancellationToken> WaitForToolStartAsync(
        TaskCompletionSource<CancellationToken> toolStarted, Task executeTask)
    {
        var first = await Task.WhenAny(toolStarted.Task, executeTask, Task.Delay(ToolStartBound));
        first.ShouldBe(
            toolStarted.Task,
            $"the tool never started (agent run {executeTask.Status}: {executeTask.Exception?.GetBaseException().Message})");
        return await toolStarted.Task;
    }

    [Fact]
    public void GracefulShutdownGracePeriod_IsFiveSeconds()
    {
        // 验证宽限期常量为 5 秒
        AgentExecutor.GracefulShutdownGracePeriod.ShouldBe(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// 简单的 Mock IChatClient 用于测试
    /// </summary>
    private class MockChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, ChatResponse> _handler;

        public MockChatClient(Func<IEnumerable<ChatMessage>, ChatResponse> handler)
        {
            _handler = handler;
        }

        public ChatClientMetadata Metadata => new();

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_handler(chatMessages));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
