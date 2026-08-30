namespace Tnzi.AI.Options;

/// <summary>
/// LLM 提供商注册表 —— 绑定 <c>AI</c> 配置节的 <c>DefaultProvider</c> 与 <c>Providers</c> 两项。
/// </summary>
/// <remarks>
/// ★这是核心（<c>Tnzi</c> 程序集）对 <c>AI</c> 配置节的**最小投影**：只取「跟哪个 LLM 说话」
/// 所需的部分，使消费应用不加载 <c>Tnzi.AI</c> 模块也能用 <see cref="Services.IAiUtility"/>。
/// 加载 <c>Tnzi.AI</c> 后，<c>AIOptions</c> 绑定同一节的完整视图（含 Agent 层配置），
/// 二者并行存在且都以配置文件为唯一事实源 —— 配置文件的格式因此保持不变。
/// </remarks>
[ConfigSection("AI")]
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public class AiProviderRegistryOptions
{
    /// <summary>
    /// 默认提供商名称，须是 <see cref="Providers"/> 中的键。
    /// </summary>
    public string DefaultProvider { get; set; } = "OpenAI";

    /// <summary>
    /// 提供商配置字典，键为提供商名称。
    /// </summary>
    public Dictionary<string, AiProviderOptions> Providers { get; set; } = new();

    /// <summary>
    /// 按名称解析已启用的提供商；未找到或未启用时返回 <see langword="null"/>。
    /// </summary>
    /// <param name="providerName">提供商名称，为空时使用 <see cref="DefaultProvider"/>。</param>
    public AiProviderOptions? ResolveEnabled(string? providerName = null)
    {
        var name = string.IsNullOrWhiteSpace(providerName) ? DefaultProvider : providerName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return Providers.TryGetValue(name, out var options) && options.Enabled ? options : null;
    }
}
