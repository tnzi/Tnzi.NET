namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// DeepSeek 消息处理器 — 处理 reasoning_content 字段。
/// </summary>
/// <remarks>
/// 当前为直通实现（passthrough），MEAI 抽象已处理大部分差异。
/// 后续需要时可在此实现 DeepSeek 特有的消息预处理/后处理逻辑（如 reasoning_content 回传格式化）。
/// </remarks>
public class DeepSeekChatMessageProcessor : IChatMessageProcessor
{
    public string ProviderName => "deepseek";

    // passthrough — MEAI SDK 已自动处理 reasoning_content 传递
    public IReadOnlyList<ChatMessage> ProcessOutgoing(IEnumerable<ChatMessage> messages) => messages.ToList();
    public IReadOnlyList<ChatMessage> ProcessIncoming(IEnumerable<ChatMessage> messages) => messages.ToList();
}
