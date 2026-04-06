using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Tnzi.AI.Tests.Integration;

/// <summary>
/// 可编程的 Mock ChatClient Provider — 支持队列式响应预设
/// </summary>
public class MockChatClientProvider : IChatClientProvider
{
    public string ProviderName => "MockProvider";

    private readonly ConcurrentQueue<QueuedResponse> _responseQueue = new();

    /// <summary>
    /// 预设一个文本响应
    /// </summary>
    public void EnqueueResponse(string text)
    {
        _responseQueue.Enqueue(new QueuedResponse { Text = text });
    }

    /// <summary>
    /// 预设一个工具调用响应
    /// </summary>
    public void EnqueueToolCall(string toolName, object args)
    {
        _responseQueue.Enqueue(new QueuedResponse
        {
            ToolCallName = toolName,
            ToolCallArgs = JsonSerializer.Serialize(args)
        });
    }

    /// <summary>
    /// 预设一个工具调用 + 最终文本响应的对话序列
    /// （第一次 GetResponse 返回工具调用，第二次返回最终文本）
    /// </summary>
    public void EnqueueToolUseConversation(string toolName, object args, string finalResponse)
    {
        EnqueueToolCall(toolName, args);
        EnqueueResponse(finalResponse);
    }

    public IChatClient CreateChatClient(ProviderOptions options, string model)
    {
        return new QueuedMockChatClient(_responseQueue);
    }

    public IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator(ProviderOptions options, string model)
    {
        return null;
    }

    /// <summary>
    /// 排队中的响应数量
    /// </summary>
    public int PendingResponseCount => _responseQueue.Count;

    private sealed class QueuedResponse
    {
        public string? Text { get; init; }
        public string? ToolCallName { get; init; }
        public string? ToolCallArgs { get; init; }
        public bool IsToolCall => ToolCallName != null;
    }

    /// <summary>
    /// 基于队列的 Mock IChatClient，按预设顺序返回响应
    /// </summary>
    private sealed class QueuedMockChatClient : IChatClient
    {
        private readonly ConcurrentQueue<QueuedResponse> _queue;

        public QueuedMockChatClient(ConcurrentQueue<QueuedResponse> queue)
        {
            _queue = queue;
        }

        public ChatClientMetadata Metadata => new("MockProvider", null, "mock-model");

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;

            if (!_queue.TryDequeue(out var response))
            {
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "No more queued responses"))
                {
                    FinishReason = ChatFinishReason.Stop
                };
            }

            if (response.IsToolCall)
            {
                return CreateToolCallResponse(response);
            }

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, response.Text ?? string.Empty))
            {
                FinishReason = ChatFinishReason.Stop,
                Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 20 }
            };
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;

            if (!_queue.TryDequeue(out var response))
            {
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent("No more queued responses")],
                    FinishReason = ChatFinishReason.Stop
                };
                yield break;
            }

            if (response.IsToolCall)
            {
                // 流式工具调用：返回单个包含 FunctionCallContent 的 update
                var toolCallContent = new FunctionCallContent(
                    Guid.NewGuid().ToString(),
                    response.ToolCallName!,
                    new Dictionary<string, object?>(
                        JsonSerializer.Deserialize<Dictionary<string, object?>>(response.ToolCallArgs!)
                        ?? new Dictionary<string, object?>()));

                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [toolCallContent]
                };
                yield break;
            }

            // 流式文本：逐字符返回
            var text = response.Text ?? string.Empty;
            foreach (var ch in text)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent(ch.ToString())]
                };
            }

            // 最终 chunk 带 finish reason 和 usage
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                FinishReason = ChatFinishReason.Stop,
                Contents = [new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 20 })]
            };
        }

        private static ChatResponse CreateToolCallResponse(QueuedResponse response)
        {
            var toolCallContent = new FunctionCallContent(
                Guid.NewGuid().ToString(),
                response.ToolCallName!,
                new Dictionary<string, object?>(
                    JsonSerializer.Deserialize<Dictionary<string, object?>>(response.ToolCallArgs!)
                    ?? new Dictionary<string, object?>()));

            var message = new ChatMessage(ChatRole.Assistant, [toolCallContent]);
            return new ChatResponse(message)
            {
                FinishReason = ChatFinishReason.ToolCalls
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceType == typeof(IChatClient)) return this;
            return null;
        }

        public void Dispose()
        {
        }
    }
}
