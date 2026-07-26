namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// Gemini 消息处理器 - 处理 thought_signature 字段。
/// </summary>
/// <remarks>
/// 当前为直通实现（passthrough），MEAI 抽象已处理大部分差异。
/// 后续需要时可在此实现 Gemini 特有的消息格式化（如 thought_signature 多轮回传强制匹配）。
/// </remarks>
public class GeminiChatMessageProcessor : IChatMessageProcessor
{
    public string ProviderName => "gemini";

    // passthrough - MEAI SDK 已自动处理 thought_signature 传递
    public IReadOnlyList<ChatMessage> ProcessOutgoing(IEnumerable<ChatMessage> messages) => messages.ToList();
    public IReadOnlyList<ChatMessage> ProcessIncoming(IEnumerable<ChatMessage> messages) => messages.ToList();
}
