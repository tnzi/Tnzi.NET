namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// Chat 消息处理器 - 处理特定 AI 提供商的消息格式差异。
/// </summary>
/// <remarks>
/// 不同 AI 提供商在消息格式上存在差异（如 DeepSeek 的 reasoning_content、
/// Gemini 的 thought_signature、MiniMax 的 inline think 标签）。
/// 此接口允许为每个提供商注册专用的消息预处理/后处理逻辑。
/// </remarks>
public interface IChatMessageProcessor
{
    /// <summary>提供商名称（如 "deepseek", "gemini", "minimax"）</summary>
    string ProviderName { get; }

    /// <summary>
    /// 处理发送给 AI 的消息（预处理）
    /// </summary>
    /// <param name="messages">原始消息列表</param>
    /// <returns>处理后的消息列表</returns>
    IReadOnlyList<ChatMessage> ProcessOutgoing(IEnumerable<ChatMessage> messages);

    /// <summary>
    /// 处理从 AI 返回的消息（后处理）
    /// </summary>
    /// <param name="messages">AI 返回的消息列表</param>
    /// <returns>处理后的消息列表</returns>
    IReadOnlyList<ChatMessage> ProcessIncoming(IEnumerable<ChatMessage> messages);
}
