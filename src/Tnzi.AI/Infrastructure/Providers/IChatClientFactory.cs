namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// ChatClient 工厂接口 — 返回 MEAI 抽象类型
/// </summary>
/// <remarks>
/// 接口只暴露 MEAI 抽象方法。需要 OpenAI SDK 类型时，
/// 使用 <see cref="ChatClientFactoryExtensions"/> 中的扩展方法。
/// </remarks>
public interface IChatClientFactory
{
    /// <summary>
    /// 获取 IChatClient（MEAI 抽象）
    /// </summary>
    IChatClient GetChatClient(string? providerName = null, string? model = null);

    /// <summary>
    /// 获取 IEmbeddingGenerator（MEAI 抽象）
    /// </summary>
    IEmbeddingGenerator<string, Embedding<float>>? GetEmbeddingGenerator(string? providerName = null, string? model = null);

    /// <summary>
    /// 获取所有已配置且启用的提供商名称列表
    /// </summary>
    IReadOnlyList<string> GetAvailableProviders() => [];

    /// <summary>
    /// 获取指定提供商的默认模型名称
    /// </summary>
    string? GetDefaultModel(string? providerName = null) => null;
}
