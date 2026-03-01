namespace Tnzi.AI.Extensions;

/// <summary>
/// ChatClientFactory 扩展方法 — 提供 OpenAI SDK 类型的向后兼容访问
/// </summary>
/// <remarks>
/// 这些方法仅在 ChatClientFactory 具体类型上可用（非接口），
/// 确保调用方明确知道自己依赖 OpenAI SDK 类型。
/// </remarks>
public static class ChatClientFactoryExtensions
{
    /// <summary>
    /// 获取 OpenAI ChatClient（仅 OpenAI 兼容提供商可用）
    /// </summary>
    public static OpenAI.Chat.ChatClient GetOpenAIChatClient(this ChatClientFactory factory, string? providerName = null, string? model = null)
    {
        Check.NotNull(factory);
        return factory.GetOpenAIChatClientInternal(providerName, model);
    }

    /// <summary>
    /// 获取 OpenAI EmbeddingClient（仅 OpenAI 兼容提供商可用）
    /// </summary>
    public static OpenAI.Embeddings.EmbeddingClient GetOpenAIEmbeddingClient(this ChatClientFactory factory, string? providerName = null, string? model = null)
    {
        Check.NotNull(factory);
        return factory.GetOpenAIEmbeddingClientInternal(providerName, model);
    }
}
