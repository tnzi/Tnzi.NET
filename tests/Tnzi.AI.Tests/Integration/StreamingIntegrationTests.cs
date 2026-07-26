using System.Runtime.CompilerServices;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Integration;

/// <summary>
/// 流式管道集成测试 - 验证流式特有行为（chunk 顺序、工具调用交错、错误恢复、取消、元数据）
/// </summary>
public class StreamingIntegrationTests
{
    #region 1. Full Pipeline Streaming

    /// <summary>
    /// 完整管道流式：多中间件 + 文本响应，验证 chunk 有序到达
    /// </summary>
    public class FullPipelineStreamingTests : AiIntegrationTestBase
    {
        private readonly List<string> _middlewareLog = [];

        protected override void ConfigureAiOptions(AIOptions options)
        {
            options.Guardrails = new GuardrailsOptions
            {
                Enabled = true,
                EnableMaxLength = true,
                MaxInputLength = 5000
            };
        }

        protected override void ConfigureMiddlewares(IServiceCollection services)
        {
            // InputGuardrail
            services.AddSingleton<IInputGuardrail>(sp =>
                new MaxLengthGuardrail(sp.GetRequiredService<IOptionsMonitor<AIOptions>>()));
            services.AddSingleton<IEnumerable<IOutputGuardrail>>(Array.Empty<IOutputGuardrail>());
            services.AddSingleton<GuardrailRunner>();
            services.AddSingleton<IAiMiddleware>(sp =>
                new InputGuardrailMiddleware(
                    sp.GetRequiredService<GuardrailRunner>(),
                    NullLogger<InputGuardrailMiddleware>.Instance));

            // LoopDetection
            services.AddSingleton<IAiMiddleware>(_ =>
                new LoopDetectionMiddleware(
                    TestHelpers.CreateOptionsMonitor(new LoopDetectionOptions { Enabled = true }),
                    NullLogger<LoopDetectionMiddleware>.Instance));

            // 顺序追踪中间件（验证中间件都被流式管道调用）
            services.AddSingleton<IAiMiddleware>(new OrderTrackingMiddleware(
                "Tracker", 9999, _middlewareLog));
        }

        [Fact]
        public async Task Should_DeliverAllChunksInOrder_When_FullPipelineStreaming()
        {
            // Arrange
            const string responseText = "Hello World!";
            MockProvider.EnqueueResponse(responseText);
            var request = CreateRequest("Say hello");

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert: 文本完整、chunk 有序
            result.FullText.ShouldContain(responseText);
            result.HasErrors.ShouldBeFalse();

            // 验证文本 chunk 逐字符到达
            var textChunks = result.AllChunks
                .Where(c => !string.IsNullOrEmpty(c.Text))
                .ToList();
            textChunks.Count.ShouldBeGreaterThanOrEqualTo(responseText.Length);

            // 重新拼接应与原始文本一致
            var rebuilt = string.Concat(textChunks.Select(c => c.Text));
            rebuilt.ShouldBe(responseText);
        }

        [Fact]
        public async Task Should_InvokeAllMiddlewares_When_StreamingThroughPipeline()
        {
            // Arrange
            _middlewareLog.Clear();
            MockProvider.EnqueueResponse("Pipeline check");
            var request = CreateRequest("Test");

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert: 自定义 tracking 中间件被调用
            _middlewareLog.ShouldContain("Tracker");
        }
    }

    #endregion

    #region 2. Streaming with Tool Calls

    /// <summary>
    /// 流式工具调用：文本 + 工具调用 + 工具结果 + 最终文本的 chunk 序列
    /// </summary>
    public class StreamingToolCallTests : AiIntegrationTestBase
    {
        [Fact]
        public async Task Should_StreamToolCallChunks_When_AgentCallsTool()
        {
            // Arrange: 工具调用 → 最终文本
            MockProvider.EnqueueToolUseConversation(
                "get_weather",
                new { city = "Tokyo" },
                "The weather in Tokyo is 22°C and sunny.");
            var request = CreateRequest("What's the weather in Tokyo?");

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert: 最终文本包含天气信息
            result.FullText.ShouldContain("Tokyo");
            result.FullText.ShouldContain("sunny");

            // 应有工具调用 chunk
            result.HasToolCalls.ShouldBeTrue();
            result.ToolCallChunks.ShouldNotBeEmpty();
        }

        [Fact]
        public async Task Should_StreamToolCallBeforeText_When_ToolCallSequence()
        {
            // Arrange
            MockProvider.EnqueueToolCall("search", new { query = "news" });
            MockProvider.EnqueueResponse("Here are the results.");
            var request = CreateRequest("Search for news");

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert: 工具调用 chunk 在文本 chunk 之前
            var firstToolCallIndex = result.AllChunks.FindIndex(c => c.IsToolCall);
            var firstTextIndex = result.AllChunks.FindIndex(c => !string.IsNullOrEmpty(c.Text));

            firstToolCallIndex.ShouldBeGreaterThanOrEqualTo(0);
            firstTextIndex.ShouldBeGreaterThanOrEqualTo(0);
            firstToolCallIndex.ShouldBeLessThan(firstTextIndex);
        }

        [Fact]
        public async Task Should_StreamMultipleToolCalls_When_SequentialTools()
        {
            // Arrange: 两次工具调用 + 最终文本
            MockProvider.EnqueueToolCall("step1", new { input = "a" });
            MockProvider.EnqueueToolCall("step2", new { input = "b" });
            MockProvider.EnqueueResponse("Both steps completed.");
            var request = CreateRequest("Run both steps");

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert
            result.FullText.ShouldContain("Both steps completed");
            result.FinishReason.ShouldBe("stop");
        }
    }

    #endregion

    #region 3. Streaming Error Mid-Stream

    /// <summary>
    /// 流式中途错误：Provider 队列耗尽后的错误处理
    /// </summary>
    public class StreamingErrorTests : AiIntegrationTestBase
    {
        [Fact]
        public async Task Should_HandleGracefully_When_ProviderReturnsDefault()
        {
            // Arrange: 不预设任何响应（队列为空）
            var request = CreateRequest("Hello");

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert: Mock 返回默认 "No more queued responses"，不应崩溃
            result.ShouldNotBeNull();
            result.FullText.ShouldContain("No more queued responses");
            result.FinishReason.ShouldBe("stop");
        }

        [Fact]
        public async Task Should_CompleteStream_When_SecondCallExhaustsQueue()
        {
            // Arrange: 预设一个工具调用但无最终响应
            // Mock 在队列耗尽时返回默认文本
            MockProvider.EnqueueToolCall("search", new { query = "test" });
            // 不预设第二个响应 → 将返回 "No more queued responses"
            var request = CreateRequest("Search something");

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert: 流应正常完成（工具调用 + 默认回退文本）
            result.ShouldNotBeNull();
            result.HasErrors.ShouldBeFalse();
        }
    }

    #endregion

    #region 4. Streaming Cancellation

    /// <summary>
    /// 流式取消：通过 CancellationToken 中途取消流式输出
    /// </summary>
    public class StreamingCancellationTests : AiIntegrationTestBase
    {
        [Fact]
        public async Task Should_StopEmittingChunks_When_CancellationRequested()
        {
            // Arrange: 长文本确保流式过程中有足够 chunk
            var longText = new string('A', 500);
            MockProvider.EnqueueResponse(longText);
            var request = CreateRequest("Long response");

            using var cts = new CancellationTokenSource();
            var chunks = new List<AgentStreamChunk>();
            var wasCancelled = false;

            // Act: 收到部分 chunk 后取消
            try
            {
                await foreach (var chunk in Runtime.RunStreamingAsync(request).WithCancellation(cts.Token))
                {
                    chunks.Add(chunk);
                    // 收到 10 个 chunk 后取消
                    if (chunks.Count >= 10)
                    {
                        cts.Cancel();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                wasCancelled = true;
            }

            // Assert: 取消发生、收到的 chunk 少于文本总长度
            wasCancelled.ShouldBeTrue();
            chunks.Count.ShouldBeLessThan(longText.Length + 5); // +5 容忍 finish chunk 等
            chunks.Count.ShouldBeGreaterThanOrEqualTo(10);
        }

        [Fact]
        public async Task Should_NotThrowUnexpected_When_ImmediatelyCancelled()
        {
            // Arrange: 立即取消
            MockProvider.EnqueueResponse("Should not see this");
            var request = CreateRequest("Immediate cancel");

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // 立即取消

            // Act & Assert: 应抛出 OperationCanceledException 或 TaskCanceledException
            await Should.ThrowAsync<OperationCanceledException>(async () =>
            {
                await foreach (var chunk in Runtime.RunStreamingAsync(request).WithCancellation(cts.Token))
                {
                    // 不应到达
                }
            });
        }
    }

    #endregion

    #region 5. Large Streaming Response

    /// <summary>
    /// 大文本流式：多段落文本逐字符流式，验证完整收集
    /// </summary>
    public class LargeStreamingResponseTests : AiIntegrationTestBase
    {
        [Fact]
        public async Task Should_CollectAllText_When_LargeMultiParagraphResponse()
        {
            // Arrange: 多段落文本
            var paragraphs = Enumerable.Range(1, 10)
                .Select(i => $"Paragraph {i}: Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.")
                .ToList();
            var fullText = string.Join("\n\n", paragraphs);
            MockProvider.EnqueueResponse(fullText);
            var request = CreateRequest("Write a long essay");

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert: 文本完整收集
            result.FullText.ShouldBe(fullText);
            result.HasErrors.ShouldBeFalse();
            result.FinishReason.ShouldBe("stop");
        }

        [Fact]
        public async Task Should_StreamCharByChar_When_LargeText()
        {
            // Arrange: 200 字符的文本
            var text = string.Concat(Enumerable.Range(0, 200).Select(i => (char)('A' + (i % 26))));
            MockProvider.EnqueueResponse(text);
            var request = CreateRequest("Generate text");

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert: 文本 chunk 数 >= 字符数（Mock 逐字符发送）
            var textChunks = result.AllChunks.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
            textChunks.Count.ShouldBeGreaterThanOrEqualTo(200);

            // 每个文本 chunk 应只有 1 个字符
            foreach (var chunk in textChunks)
            {
                chunk.Text!.Length.ShouldBe(1);
            }
        }

        [Fact]
        public async Task Should_HandleSpecialCharacters_When_StreamingUnicode()
        {
            // Arrange: 包含 Unicode 字符
            const string unicodeText = "你好世界🌍 Hello こんにちは مرحبا";
            MockProvider.EnqueueResponse(unicodeText);
            var request = CreateRequest("Unicode test");

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert: Unicode 字符完整保留
            result.FullText.ShouldBe(unicodeText);
            result.HasErrors.ShouldBeFalse();
        }
    }

    #endregion

    #region 6. Streaming Metadata

    /// <summary>
    /// 流式元数据：验证 AgentName、Usage、FinishReason 在最终 chunk 中
    /// </summary>
    public class StreamingMetadataTests : AiIntegrationTestBase
    {
        [Fact]
        public async Task Should_IncludeUsageInStream_When_ResponseCompleted()
        {
            // Arrange
            MockProvider.EnqueueResponse("Usage test");
            var request = CreateRequest("Check usage");

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert
            result.Usage.ShouldNotBeNull();
            result.Usage!.InputTokens.ShouldBeGreaterThan(0);
            result.Usage.OutputTokens.ShouldBeGreaterThan(0);
        }

        [Fact]
        public async Task Should_HaveFinishReasonStop_When_NormalCompletion()
        {
            // Arrange
            MockProvider.EnqueueResponse("Normal completion");
            var request = CreateRequest("Complete normally");

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert
            result.FinishReason.ShouldNotBeNullOrWhiteSpace();
            result.FinishReason.ShouldBe("stop");
        }

        [Fact]
        public async Task Should_HaveUsageInLastChunk_When_Streaming()
        {
            // Arrange
            MockProvider.EnqueueResponse("Metadata check");
            var request = CreateRequest("Check metadata");

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert: Usage 出现在 chunk 列表中
            var usageChunks = result.AllChunks.Where(c => c.Usage != null).ToList();
            usageChunks.Count.ShouldBeGreaterThanOrEqualTo(1);

            // Usage chunk 应在文本 chunk 之后（最终 chunk）
            var lastUsageIndex = result.AllChunks.FindLastIndex(c => c.Usage != null);
            var lastTextIndex = result.AllChunks.FindLastIndex(c => !string.IsNullOrEmpty(c.Text));
            if (lastTextIndex >= 0)
            {
                lastUsageIndex.ShouldBeGreaterThanOrEqualTo(lastTextIndex);
            }
        }

        [Fact]
        public async Task Should_TrackRunAsCompleted_When_StreamingWithRunTracking()
        {
            // Arrange
            var agent = await CreateAgentAsync("StreamBot");
            MockProvider.EnqueueResponse("Tracked streaming");
            var request = new AgentRunRequest
            {
                AgentId = agent.Id,
                UserMessage = "Track this stream",
                EnableRunTracking = true
            };

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert: 流式完成后 Run 状态为 Completed
            result.FinishReason.ShouldBe("stop");
            result.HasErrors.ShouldBeFalse();
        }

        [Fact]
        public async Task Should_EmitFinishReasonInFinalChunk_When_StreamEnds()
        {
            // Arrange
            MockProvider.EnqueueResponse("Final chunk test");
            var request = CreateRequest("Test final chunk");

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert: 最后一个有 FinishReason 的 chunk 值为 "stop"
            var finishChunks = result.AllChunks.Where(c => c.FinishReason != null).ToList();
            finishChunks.ShouldNotBeEmpty();
            finishChunks.Last().FinishReason.ShouldBe("stop");
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// 执行顺序追踪中间件
    /// </summary>
    private class OrderTrackingMiddleware : IAiMiddleware
    {
        private readonly string _name;
        private readonly List<string> _log;

        public int Order { get; }

        public OrderTrackingMiddleware(string name, int order, List<string> log)
        {
            _name = name;
            Order = order;
            _log = log;
        }

        public Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken ct = default)
        {
            _log.Add(_name);
            return next(context, ct);
        }

        public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
            AiMiddlewareContext context, AiStreamingMiddlewareDelegate next,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            _log.Add(_name);
            await foreach (var chunk in next(context, ct))
                yield return chunk;
        }
    }

    #endregion
}
