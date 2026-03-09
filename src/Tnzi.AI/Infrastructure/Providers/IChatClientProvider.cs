namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// ChatClient 提供商接口 — 可插拔的 AI 提供商抽象
/// </summary>
/// <remarks>
/// 每个提供商（OpenAI、Azure、Anthropic 等）实现此接口。
/// ChatClientFactory 通过 DI 收集所有注册的 Provider 并按名称分发。
/// </remarks>
public interface IChatClientProvider
{
    /// <summary>
    /// 提供商名称（与配置中的 Providers 字典键匹配）
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// 创建 IChatClient 实例
    /// </summary>
    /// <param name="options">提供商配置选项</param>
    /// <param name="model">模型名称</param>
    /// <returns>MEAI IChatClient 实例</returns>
    IChatClient CreateChatClient(ProviderOptions options, string model);

    /// <summary>
    /// 创建 IEmbeddingGenerator 实例（可选，部分提供商不支持）
    /// </summary>
    /// <param name="options">提供商配置选项</param>
    /// <param name="model">模型名称</param>
    /// <returns>嵌入生成器，不支持时返回 null</returns>
    IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator(ProviderOptions options, string model);

    /// <summary>
    /// 创建底层原生 SDK 客户端（可选，用于需要原生 SDK 功能的场景）
    /// </summary>
    /// <param name="options">提供商配置选项</param>
    /// <returns>原生客户端对象，不支持时返回 null</returns>
    object? CreateNativeClient(ProviderOptions options) => null;
}
