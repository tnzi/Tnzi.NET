namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// ChatClient 工厂接口 - 返回 MEAI 抽象类型
/// </summary>
/// <remarks>
/// 接口只暴露 MEAI 抽象方法（IChatClient / IEmbeddingGenerator）。
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
    /// 获取指定提供商的默认模型名称（与 <see cref="GetChatClient"/> 不传模型时用的是同一个）；解析不到时为 null
    /// </summary>
    string? GetDefaultModel(string? providerName = null) => null;

    /// <summary>
    /// 指定名字的提供商此刻能否为调用方解析出来，与 <see cref="GetChatClient"/> 同一口径。
    /// </summary>
    /// <remarks>
    /// <see cref="GetAvailableProviders"/> 只列配置来源的提供商；管理端登记在库里的提供商要按名问这一句，
    /// 否则 Agent 校验会把一个能正常对话的提供商报成「未注册」。默认实现退回配置名单，供不支持运行时登记的实现沿用。
    /// </remarks>
    bool IsProviderAvailable(string providerName)
        => GetAvailableProviders().Contains(providerName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 使某个名字的提供商在本工厂的全部缓存（数据库解析结果与已建客户端）失效，下次解析重新读库。
    /// Provider CRUD 后调用；清掉的是<b>所有租户桶</b>里叫这个名字的条目（调用方只带得出名字）。
    /// 默认实现为空操作，供不缓存的实现沿用。
    /// </summary>
    void InvalidateProvider(string providerName)
    {
    }
}
