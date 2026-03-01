namespace Tnzi.AI.Tests;

/// <summary>
/// AgentExecutor 单元测试 — 验证 FindTool 搜索 context-injected 工具
/// </summary>
public class AgentExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WithContextInjectedTool_CanFindAndExecuteTool()
    {
        // Arrange: 创建一个 context-injected 工具
        var contextToolCalled = false;
        var contextTool = AIFunctionFactory.Create(() =>
        {
            contextToolCalled = true;
            return "context tool result";
        }, "skill_search", "Search skills");

        // 创建 ContextProvider，注入工具
        var contextProvider = new Mock<IContextProvider>();
        contextProvider.Setup(p => p.GetContextAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextInjection { Tools = [contextTool] });
        contextProvider.Setup(p => p.OnCompletedAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // 第一次调用: LLM 返回工具调用
        var toolCallContent = new FunctionCallContent("call_1", "skill_search", new Dictionary<string, object?>());
        var assistantMsgWithToolCall = new ChatMessage(ChatRole.Assistant, [toolCallContent]);

        // 第二次调用: LLM 返回最终文本
        var finalMsg = new ChatMessage(ChatRole.Assistant, "Done");

        var callCount = 0;
        var mockClient = new Mock<IChatClient>();
        mockClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return new ChatResponse([assistantMsgWithToolCall]);
                }
                return new ChatResponse([finalMsg]);
            });

        var options = new AgentExecutorOptions
        {
            Name = "TestAgent",
            Tools = [], // 无内置工具 — 只有 context-injected 工具
            ContextProvider = contextProvider.Object
        };

        var executor = new AgentExecutor(mockClient.Object, options);

        // Act
        var messages = new List<ChatMessage> { new(ChatRole.User, "Find a skill") };
        var response = await executor.ExecuteAsync(messages, CancellationToken.None);

        // Assert
        contextToolCalled.ShouldBeTrue(); // context-injected 工具应该被成功调用
        response.Text.ShouldBe("Done");
        callCount.ShouldBe(2); // LLM 被调用 2 次（工具调用 + 最终回复）
    }

    [Fact]
    public async Task ExecuteAsync_WithOptionsToolsOnly_FindsToolNormally()
    {
        // Arrange: 工具在 Options 中注册（不使用 ContextProvider）
        var optionsToolCalled = false;
        var optionsTool = AIFunctionFactory.Create(() =>
        {
            optionsToolCalled = true;
            return "options tool result";
        }, "calculator", "Calculate");

        // 第一次: 工具调用，第二次: 最终回复
        var toolCallContent = new FunctionCallContent("call_1", "calculator", new Dictionary<string, object?>());
        var assistantMsgWithToolCall = new ChatMessage(ChatRole.Assistant, [toolCallContent]);
        var finalMsg = new ChatMessage(ChatRole.Assistant, "42");

        var callCount = 0;
        var mockClient = new Mock<IChatClient>();
        mockClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new ChatResponse([assistantMsgWithToolCall])
                    : new ChatResponse([finalMsg]);
            });

        var options = new AgentExecutorOptions
        {
            Name = "TestAgent",
            Tools = [optionsTool]
        };

        var executor = new AgentExecutor(mockClient.Object, options);

        // Act
        var messages = new List<ChatMessage> { new(ChatRole.User, "What is 6*7?") };
        var response = await executor.ExecuteAsync(messages, CancellationToken.None);

        // Assert
        optionsToolCalled.ShouldBeTrue();
        response.Text.ShouldBe("42");
    }

    [Fact]
    public async Task ExecuteAsync_ToolNotFound_ReturnsErrorMessageToLLM()
    {
        // Arrange: LLM 调用一个不存在的工具
        var toolCallContent = new FunctionCallContent("call_1", "nonexistent_tool", new Dictionary<string, object?>());
        var assistantMsgWithToolCall = new ChatMessage(ChatRole.Assistant, [toolCallContent]);
        var finalMsg = new ChatMessage(ChatRole.Assistant, "I couldn't find that tool");

        var callCount = 0;
        var mockClient = new Mock<IChatClient>();
        mockClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new ChatResponse([assistantMsgWithToolCall])
                    : new ChatResponse([finalMsg]);
            });

        var options = new AgentExecutorOptions
        {
            Name = "TestAgent",
            Tools = []
        };

        var executor = new AgentExecutor(mockClient.Object, options);

        // Act
        var messages = new List<ChatMessage> { new(ChatRole.User, "Use some tool") };
        var response = await executor.ExecuteAsync(messages, CancellationToken.None);

        // Assert — LLM 应该收到工具未找到的错误消息，并继续生成回复
        response.Text.ShouldBe("I couldn't find that tool");
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task ExecuteAsync_NoToolCalls_ReturnsDirectly()
    {
        // Arrange: LLM 直接返回文本，无工具调用
        var mockClient = new Mock<IChatClient>();
        mockClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello!")));

        var options = new AgentExecutorOptions { Name = "TestAgent" };
        var executor = new AgentExecutor(mockClient.Object, options);

        // Act
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };
        var response = await executor.ExecuteAsync(messages, CancellationToken.None);

        // Assert
        response.Text.ShouldBe("Hello!");
        response.FinishReason.ShouldNotBe("max_tool_iterations");
    }
}
