namespace Tnzi.AI.Tests;

/// <summary>
/// AgentExecutor 单元测试 - 验证 FindTool 搜索 context-injected 工具
/// </summary>
public class AgentExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WithAdditionalToolFromRuntime_CanFindAndExecuteTool()
    {
        // Arrange: 模拟 AgentRuntime.MergeAdditionalTools - context-injected 工具
        // 在新架构下由 ContextInjectionMiddleware 写入 AiMiddlewareContext.AdditionalTools，
        // 然后 AgentRuntime 通过 AgentExecutor.WithAdditionalTools 合并到 Options.Tools。
        // AgentExecutor 自身不再持有 IContextProvider 引用。
        var contextToolCalled = false;
        var contextTool = AIFunctionFactory.Create(() =>
        {
            contextToolCalled = true;
            return "context tool result";
        }, "skill_search", "Search skills");

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
            Tools = [] // 无内置工具
        };

        var executor = new AgentExecutor(mockClient.Object, options)
            .WithAdditionalTools([contextTool]); // runtime-side merge - what AgentRuntime does

        // Act
        var messages = new List<ChatMessage> { new(ChatRole.User, "Find a skill") };
        var response = await executor.ExecuteAsync(messages, CancellationToken.None);

        // Assert
        contextToolCalled.ShouldBeTrue();
        response.Text.ShouldBe("Done");
        callCount.ShouldBe(2);
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

        // Assert - LLM 应该收到工具未找到的错误消息，并继续生成回复
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

    [Fact]
    public async Task ExecuteStreamingAsync_WithToolRecoveryMiddleware_ReportsFailedToolCall()
    {
        var brokenTool = AIFunctionFactory.Create(() =>
        {
            throw new InvalidOperationException("boom");
#pragma warning disable CS0162
            return "unreachable";
#pragma warning restore CS0162
        }, "broken_tool", "Broken");

        var firstUpdate = new ChatResponseUpdate
        {
            Contents = [new FunctionCallContent("call_1", "broken_tool", new Dictionary<string, object?>())]
        };
        var secondUpdate = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            FinishReason = ChatFinishReason.Stop,
            Contents = [new TextContent("handled")]
        };

        var mockClient = new Mock<IChatClient>();
        mockClient.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateStreamingResponse(firstUpdate, secondUpdate));

        var executor = new AgentExecutor(mockClient.Object, new AgentExecutorOptions
        {
            Name = "TestAgent",
            Tools = [brokenTool],
            Middlewares = [new ToolErrorRecoveryMiddleware(NullLogger<ToolErrorRecoveryMiddleware>.Instance)]
        });

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in executor.ExecuteStreamingAsync([new ChatMessage(ChatRole.User, "run tool")]))
        {
            chunks.Add(chunk);
        }

        var toolDetails = chunks.Last(c => c.ToolCalls is { Count: > 0 }).ToolCalls!;
        toolDetails.Count.ShouldBe(1);
        toolDetails[0].Name.ShouldBe("broken_tool");
        toolDetails[0].IsSuccess.ShouldBeFalse();
        toolDetails[0].Error!.ShouldContain("InvalidOperationException");
    }

    // ── 回归锁定：系统提示最终形态必须与 13-tag 接缝删除前逐字节一致 ──────────
    // 删除前由 SystemPromptTemplateBuilder 装配（无 SectionProviders 的生产路径），
    // 快照为 "<instructions>\n{content}\n</instructions>"。

    [Fact]
    public async Task ExecuteAsync_StaticInstructions_ProducesExactWrappedSystemPrompt()
    {
        var mockClient = new Mock<IChatClient>();
        ChatOptions? capturedOptions = null;
        mockClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<ChatMessage> _, ChatOptions? opts, CancellationToken _) =>
            {
                capturedOptions = opts;
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done"));
            });

        var executor = new AgentExecutor(mockClient.Object, new AgentExecutorOptions
        {
            Name = "TestAgent",
            Instructions = "Base instructions."
        });

        await executor.ExecuteAsync([new ChatMessage(ChatRole.User, "Hello")], CancellationToken.None);

        capturedOptions.ShouldNotBeNull();
        capturedOptions!.Instructions.ShouldBe("<instructions>\nBase instructions.\n</instructions>");
    }

    [Fact]
    public async Task ExecuteAsync_WhitespaceInstructions_LeavesSystemPromptNull()
    {
        var mockClient = new Mock<IChatClient>();
        ChatOptions? capturedOptions = null;
        mockClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<ChatMessage> _, ChatOptions? opts, CancellationToken _) =>
            {
                capturedOptions = opts;
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done"));
            });

        var executor = new AgentExecutor(mockClient.Object, new AgentExecutorOptions
        {
            Name = "TestAgent",
            Instructions = "   "
        });

        await executor.ExecuteAsync([new ChatMessage(ChatRole.User, "Hello")], CancellationToken.None);

        capturedOptions.ShouldNotBeNull();
        capturedOptions!.Instructions.ShouldBeNull();
    }

    // ── C2: immutability - caller's messages list must not be mutated ─────────

    [Fact]
    public async Task ExecuteAsync_DoesNotMutateCallerMessages()
    {
        // Arrange: LLM does one tool call then returns final answer
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

        var tool = AIFunctionFactory.Create(() => "100", "calculator", "Calculate");
        var options = new AgentExecutorOptions { Name = "TestAgent", Tools = [tool] };
        var executor = new AgentExecutor(mockClient.Object, options);

        var callerMessages = new List<ChatMessage> { new(ChatRole.User, "What is 6*7?") };
        var originalCount = callerMessages.Count;

        // Act
        var response = await executor.ExecuteAsync(callerMessages, CancellationToken.None);

        // Assert - caller list is untouched
        callerMessages.Count.ShouldBe(originalCount,
            "ExecuteAsync must not mutate the caller-supplied messages list");
        response.Text.ShouldBe("42");
        // The response carries its own messages list (distinct object)
        response.Messages.ShouldNotBeSameAs(callerMessages);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_DoesNotMutateCallerMessages()
    {
        // Arrange: LLM does one tool call then returns final answer (streaming)
        var toolCallContent = new FunctionCallContent("call_1", "calculator", new Dictionary<string, object?>());

        var callCount = 0;
        var mockClient = new Mock<IChatClient>();
        mockClient.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return CreateStreamingResponse(
                        new ChatResponseUpdate(ChatRole.Assistant, [toolCallContent]));
                }
                return CreateStreamingResponse(
                    new ChatResponseUpdate(ChatRole.Assistant, "42"));
            });

        var tool = AIFunctionFactory.Create(() => "100", "calculator", "Calculate");
        var options = new AgentExecutorOptions { Name = "TestAgent", Tools = [tool] };
        var executor = new AgentExecutor(mockClient.Object, options);

        var callerMessages = new List<ChatMessage> { new(ChatRole.User, "What is 6*7?") };
        var originalCount = callerMessages.Count;

        // Act
        await foreach (var _ in executor.ExecuteStreamingAsync(callerMessages, CancellationToken.None)) { }

        // Assert - caller list is untouched
        callerMessages.Count.ShouldBe(originalCount,
            "ExecuteStreamingAsync must not mutate the caller-supplied messages list");
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateStreamingResponse(params ChatResponseUpdate[] updates)
    {
        foreach (var update in updates)
        {
            yield return update;
            await Task.Yield();
        }
    }
}
